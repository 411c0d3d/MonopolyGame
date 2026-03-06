# Architecture Decision Record — Monopoly Online

## Overview

Multiplayer Monopoly built on ASP.NET Core 10 with a SignalR hub and a React client served through a static HTML shell.
Complete game engine covering all classic rules — card system, trading, jail, rent, and buildings. Active game state is
held in memory for performance and persistence is swappable between repository implementations — either Azure Cosmos DB
for production or JSON files for local development via app settings. Authentication is handled by Microsoft Entra
External ID with social login (Google, Microsoft). Admin access is enforced by a role claim. A budget guard service
enforces Azure Container Apps free-tier consumption limits and persists monthly counters to Cosmos DB.

---

## Project Structure

### Server — `MonopolyServer/`

```
MonopolyServer/
├── Bot/
│   ├── BotDecisionEngine.cs         # Bot move evaluation and decision logic
│   └── BotTurnOrchestrator.cs       # Orchestrates full bot turn execution
├── DTOs/
│   ├── GameRoomInfo.cs
│   ├── GameStateDto.cs
│   ├── PlayerDto.cs
│   ├── PlayerLobbyInfo.cs
│   ├── PropertyDto.cs
│   ├── ServerHealthStatsDto.cs
│   └── TradeOfferDto.cs
├── Game/
│   ├── Constants/
│   │   └── GameConstants.cs         # Board layout, rent tables, card definitions
│   ├── Engine/
│   │   └── GameEngine.cs            # All game rules — stateless, validated, logged
│   └── Models/
│       ├── Enums/
│       │   ├── CardDeck.cs
│       │   ├── CardType.cs
│       │   ├── GameStatus.cs
│       │   ├── JailStrategy.cs
│       │   ├── PropertyType.cs
│       │   └── TradeStatus.cs
│       ├── Board.cs
│       ├── Card.cs
│       ├── GameState.cs
│       ├── Player.cs
│       ├── Property.cs
│       └── TradeOffer.cs
├── Services/
│   ├── CardDeckManager.cs           # Deck shuffle, draw, and return lifecycle
│   ├── GameRoomManager.cs           # Thread-safe singleton, single source of truth
│   ├── LobbyService.cs              # Lobby lifecycle — join, leave, host promotion
│   ├── TradeService.cs              # Trade orchestration, decoupled from SignalR
│   └── TurnTimerService.cs          # Per-game timer, auto-advance on idle
├── Hubs/
│   ├── AdminHub.cs                  # Admin diagnostics — requires Admin role claim
│   └── GameHub.cs                   # SignalR hub — validate · delegate · broadcast only
├── Infrastructure/
│   ├── Auth/
│   │   ├── AzureAdSettings.cs       # Typed config for Entra External ID
│   │   ├── CosmosUserRepository.cs  # Cosmos user persistence
│   │   ├── IUserRepository.cs       # User persistence contract
│   │   ├── UserClaimsTransformation.cs  # Enriches principal with Admin role from Cosmos
│   │   └── UserDocument.cs          # Cosmos user document (id = B2C objectId)
│   ├── Budget/
│   │   ├── BudgetDocument.cs        # Cosmos document tracking monthly resource consumption
│   │   ├── BudgetGuardService.cs    # BackgroundService — in-memory counters, periodic Cosmos flush
│   │   ├── BudgetMiddleware.cs      # HTTP middleware — enforces monthly request budget
│   │   ├── BudgetServiceExtensions.cs  # DI wiring — budget guard registration
│   │   └── BudgetSnapshot.cs        # Read-only consumption snapshot for admin panel
│   ├── Cosmos/
│   │   ├── CosmosGameRepository.cs  # Cosmos DB game persistence (v3 SDK)
│   │   ├── CosmosSettings.cs        # Typed config for Cosmos connection
│   │   ├── GameDocument.cs          # Cosmos document wrapper for GameState
│   │   └── StjCosmosSerializer.cs   # STJ-backed CosmosSerializer
│   ├── Persistence/
│   │   ├── FileGameRepository.cs    # File system game persistence
│   │   └── IGameRepository.cs       # Game persistence contract
│   ├── AuthServiceExtensions.cs     # DI wiring — auth, JWT, authorization
│   ├── GameCleanupService.cs        # BackgroundService — purges stale rooms every 60 s
│   ├── GameStateMapper.cs           # Maps GameState → GameStateDto
│   ├── InputValidator.cs            # Shared input guard helpers
│   ├── PersistenceServiceExtensions.cs  # DI wiring — persistence and game services
│   └── RateLimitingFilter.cs        # Per-connection rate limiting
├── Tests/
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
├── MonopolyServer.http
└── Program.cs
```

### Client — `MonopolyClient/`

```
MonopolyClient/
├── wwwroot/
│   ├── animation/
│   │   ├── animation.css            # Keyframe and transition definitions
│   │   └── animation.js             # usePlayerHop, useDiceRoll, DiceTray, ChestCardPopup
│   ├── components/
│   │   ├── board.js                 # Board component — 40-cell grid, tokens, scaled sizing
│   │   ├── header.js                # Top bar — room code, player list, connection status
│   │   ├── toasts.js                # Transient notification system
│   │   └── turn_timer.js            # Countdown display, auto-advance warning
│   ├── css/
│   │   ├── main.css                 # Core layout and game UI styles
│   │   └── site.css                 # Global resets and typography
│   ├── lib/                         # Vendored third-party scripts (SignalR, React)
│   ├── pages/
│   │   ├── admin_page.js            # Server health and diagnostics view
│   │   ├── game_page.js             # GamePage — hub wiring, all game state
│   │   ├── home_page.js             # Landing page — create or join a room
│   │   └── lobby_page.js            # Pre-game lobby — player list, start button
│   ├── utils/
│   │   ├── constants.js             # SPACES, COLORS, BCOLORS
│   │   └── hub_service.js           # SignalR connection factory and helpers
│   ├── app.js                       # Root component and page router
│   ├── favicon.ico
│   ├── globals.js                   # React hook aliases on window
│   ├── index.html                   # Shell page; loads React bundles via script tags
│   └── jsconfig.json
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
└── Program.cs
```

---

## Core Game Systems

### Models — Data Only

Plain containers. No business logic lives in models.

**GameState** — live game instance  
`GameId` · `HostId` · `Status` (Waiting | InProgress | Finished | Paused) · `Board` · `Players` · `CurrentPlayerIndex` ·
`Turn` · `CreatedAt` · `StartedAt` · `EndedAt` · `GameLog` · `LastDiceRoll` · `DoubleRolled` · `PendingTrades`  
Getters only: `GetCurrentPlayer()` · `GetPlayerById()` · `LogAction()`

**Player** — per-player state  
`Id` (B2C objectId for authenticated players, GUID for bots) · `Name` · `Cash` · `Position` · `IsInJail` · `JailTurnsRemaining` · `KeptCards` · `IsBankrupt` ·
`IsCurrentPlayer` · `HasRolledDice`  
Mutations only: `AddCash()` · `DeductCash()` · `MoveTo()` · `SendToJail()`

**Board** — 40-space board  
`Spaces[40]` with `GetProperty()` · `GetPropertiesByOwner()` · `GetPropertiesByColorGroup()`

**Property** — individual space  
`Id` · `Name` · `Type` (Street | Railroad | Utility | Tax | Chance | CommunityChest | FreeParking | GoToJail | Jail |
Go) · `OwnerId` · `IsMortgaged` · `HouseCount` · `HasHotel` · `PurchasePrice` · `MortgageValue` · `HouseCost` ·
`RentValues[]` · `ColorGroup`

**TradeOffer** — pending trade between two players  
`Id` · `FromPlayerId` · `ToPlayerId` · `Status` (Pending | Accepted | Rejected | Cancelled) · `OfferedPropertyIds` ·
`OfferedCash` · `RequestedPropertyIds` · `RequestedCash` · `CreatedAt` · `RespondedAt`

---

### Game Engine — All Business Logic

Single `GameEngine` per game instance. Receives `GameState`, mutates it, returns nothing. Every public method validates
preconditions before touching state and appends to `GameLog`.

**Turn & movement:** `RollDice()` · `MovePlayer(diceTotal)` · `HandleLandingOnSpace(player, position)`

**Property:** `BuyProperty()` · `BuildHouse()` · `BuildHotel()` · `MortgageProperty()` · `UnmortgageProperty()`

**Financial:** `CalculateRent(property, diceRoll)` — streets (base + per house/hotel, doubled for monopoly),
railroads ($25 × owned), utilities (dice × 4 or × 10) · `CollectRent()` · `HandleTax()`

**Cards:** `DrawAndExecuteCard(deck)` — full 32-card implementation (16 Chance + 16 Community Chest). Movement,
financial, repairs, and kept Get Out of Jail Free cards all handled. Chain effects (e.g. "Go back 3" landing on Chance)
draw the next card immediately.

**Jail:** `SendPlayerToJail()` · `ReleaseFromJail(payToBail)` — auto-release on doubles, 3-turn timeout, or $50 ·
`UseGetOutOfJailFreeCard()`

**Trading:** `ProposeTrade()` · `AcceptTrade()` (atomic transfer) · `RejectTrade()` · `CancelTrade()` — private
`ExecuteTrade()` and `ValidateTradeAssets()` guard integrity

**Game flow:** `StartGame()` · `NextTurn()` · `BankruptPlayer()` (reclaims all properties, checks win condition) ·
`EndGame(winner)`

**Card decks:** `CardDeckManager` shuffles at game start. Drawn from front, returned to back. Get Out of Jail Free cards
are held by the player and returned to the deck bottom on use. Deck reshuffles if it empties mid-game.

---

## Data Layer

Active game state is held in memory (`Dictionary<string, GameState>`) for low-latency reads and writes during play.
State is additionally persisted via `IGameRepository` — a swappable persistence contract with two implementations.

**`FileGameRepository`** — JSON file per game, stored under `/data/games/`. Per-game `SemaphoreSlim` locks serialise
concurrent writes. Used in local development and as a zero-dependency fallback.

**`CosmosGameRepository`** — Azure Cosmos DB (v3 SDK), NoSQL API, partition key `/id`. GameState is stored as a proper
nested JSON document via a custom `StjCosmosSerializer` wired into `CosmosClientOptions`, eliminating any Newtonsoft
dependency. `CosmosClient` is registered as a singleton and shared between the game and user repositories.

**`IUserRepository`** / **`CosmosUserRepository`** — users container, partition key `/id` (B2C objectId). Stores
`UserDocument` (objectId, email, displayName, isAdmin, timestamps). Created alongside the games container at startup.

The active implementation is selected via `PersistenceSettings:UseDatabase` in configuration — `false` uses
`FileGameRepository`, `true` uses `CosmosGameRepository`. Switching backends requires no changes to engine or hub code.

### Azure Cosmos DB Account — Configuration

The production Cosmos DB account lives in the default Azure directory, separate from the Monopoly411 Entra External ID
tenant. This is intentional — Cosmos and compute resources are provisioned under the default subscription, while
Monopoly411 handles only CIAM auth (JWT issuance). The two tenants do not interact at the infrastructure level.

**Account settings:**
- API: Azure Cosmos DB for NoSQL
- Capacity mode: Provisioned throughput
- Free tier discount: active (first 1,000 RU/s and 25 GB free)
- Availability zones: disabled
- Geo-redundancy: disabled
- Multi-region writes: disabled
- Backup policy: Periodic, geo-redundant storage

**Database: `MonopolyDb`**

| Container | Partition key | Throughput                 |
|-----------|---------------|----------------------------|
| `games`   | `/id`         | 1000 RU/s (shared, manual) |
| `users`   | `/id`         | shared                     |
| `budget`  | `/id`         | shared                     |

Throughput is set to 990 RU/s — just under the 1,000 RU/s free tier ceiling — shared across all three containers.
Manual throughput is used deliberately over autoscale to prevent the account from scaling beyond the free tier.

### Cosmos DB — Connection Mode

`CosmosClient` is constructed with environment-aware options. The endpoint is inspected at startup to determine whether
the target is the local emulator or the real Azure account:

```csharp
var isDevelopment = settings.Endpoint.Contains("localhost");

return new CosmosClient(settings.Endpoint, settings.AuthKey, new CosmosClientOptions
{
    Serializer        = new StjCosmosSerializer(BuildJsonOptions()),
    HttpClientFactory = isDevelopment
        ? () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        : null,
    ConnectionMode = isDevelopment ? ConnectionMode.Gateway : ConnectionMode.Direct
});
```

- **Local emulator** → `ConnectionMode.Gateway`, self-signed cert bypass via custom `HttpClientFactory`
- **Azure** → `ConnectionMode.Direct`, no cert bypass, default `HttpClientFactory`

No explicit environment check or build flag is needed — the endpoint value drives the behaviour automatically.

### Cosmos DB — Capacity and Scale Estimates

A complete game document is approximately 15–20 KB including the full board state, player records, event log, and
pending trades. An upsert costs roughly 18 RUs at that size.

| Metric | Value |
|--------|-------|
| RUs per game turn (3 saves × 18 RU) | ~54 RU |
| Safe concurrent games at 990 RU/s | ~400–500 |
| Monthly games budget (CPU-bound) | ~3,600 |
| Monthly games budget (RU-bound) | ~950,000 |
| Monthly games budget (HTTP requests) | ~8,200 |

CPU on Container Apps free tier (162,000 vCPU-seconds/month at 90% cap) is the binding constraint, not Cosmos throughput.
The `BudgetGuardService` enforces `MaxConcurrentGames = 10` which keeps the server well within all ceilings.

### Startup Ordering

```
InitializeBudgetContainerAsync()    ← ensures budget container exists
GameRoomManager.InitializeAsync()   ← awaited before app.RunAsync()
  ↓ loads all persisted games
  ↓ creates GameEngine for each InProgress game
app.RunAsync()
  ↓ hosted services start (BudgetGuardService, GameCleanupService, TurnTimerService)
```

`InitializeAsync` must complete before hosted services fire. If `GameCleanupService` runs before games are loaded
it vacuums an empty dictionary — a latent race condition that was present in the original design and is now eliminated
by explicit sequencing in `Program.cs`.

### Secrets Management

```
appsettings.json              (placeholders only, committed)
appsettings.Development.json  (non-secret dev defaults, committed)
User Secrets                  (real credentials, never committed — local dev only)
Azure Container Apps env vars (production override — injected at deploy time)
```

`.env` files were retired in favour of ASP.NET Core User Secrets for local development. Production secrets are injected
via Container Apps environment variables or Azure Key Vault references. The migration path to Managed Identity
(eliminating `AuthKey` entirely) is planned for the container deployment phase — `CosmosSettings.AuthKey` being empty
signals that `DefaultAzureCredential` should be used instead of key-based auth.

---
## Budget Guard

### Decision

Azure Container Apps free-tier resources (vCPU-seconds, HTTP requests) are finite monthly allotments. Without
enforcement, a traffic spike or runaway loop could exhaust the quota and incur unexpected charges. The budget guard
enforces hard limits in-process before any cost is incurred, with no dependency on external billing APIs.

### Architecture

All hot-path tracking is **lock-free** using `Interlocked` operations on backing fields. Cosmos persistence is
serialized under a `SemaphoreSlim` on a background timer only — never on the request path. This means the guard
adds zero meaningful latency to normal requests.

```
Request arrives
  → BudgetMiddleware.InvokeAsync
      → BudgetGuardService.TryConsumeRequest()
          → Interlocked.Increment(ref _consumedHttpRequests)   ← lock-free
          → compare against MaxHttpRequests
          → return true (allow) or false (block with 503)

Every 5 minutes (background):
  → BudgetGuardService.FlushAsync()
      → acquire _flushLock
      → accrue elapsed CPU seconds
      → sync _consumedHttpRequests into _budget document
      → MaybeResetMonth() if calendar month rolled over
      → UpsertItemAsync to Cosmos budget container
      → release _flushLock
```

On container restart, `LoadFromCosmosAsync` reloads the last persisted `BudgetDocument` and seeds
`_consumedHttpRequests` via `Interlocked.Exchange` — counters resume from the last flush point, not from zero.
Up to 5 minutes of requests since the last flush may be under-counted on an unclean restart; this is acceptable
given the 90% safety margin built into the caps.

### BudgetDocument

Single Cosmos document, `id = "server-budget"`, partition key `/id`. Contains both the configuration limits
and the running consumption counters so they travel together and reset atomically on month rollover.

| Field | Default | Notes |
|-------|---------|-------|
| `maxCpuSeconds` | 162,000 | 90% of 180,000 free-tier vCPU-seconds |
| `maxHttpRequests` | 1,800,000 | 90% of 2,000,000 free-tier requests |
| `maxConcurrentGames` | 10 | Hard cap on simultaneous active game sessions |
| `maxConcurrentConnections` | 80 | Hard cap on simultaneous SignalR connections |
| `consumedCpuSeconds` | — | Accumulated this window |
| `consumedHttpRequests` | — | Accumulated this window |
| `windowStart` | month start UTC | Drives monthly reset logic |
| `lastSavedAt` | — | Timestamp of last successful Cosmos flush |

Limits are stored in Cosmos rather than configuration so they can be adjusted at runtime without a redeploy.

### Enforcement Points

| Guard | Method | Called from |
|-------|--------|-------------|
| HTTP requests | `TryConsumeRequest()` | `BudgetMiddleware` — every incoming request |
| SignalR connections | `TryAddConnection()` / `ReleaseConnection()` | `GameHub.OnConnectedAsync` / `OnDisconnectedAsync` |
| Active games | `TryAddGame()` / `ReleaseGame()` | `GameRoomManager.CreateGame()` / `DeleteGame()` |

### HTTP 503 Response

When `TryConsumeRequest()` returns false, `BudgetMiddleware` short-circuits with:

```json
{
  "error": "Monthly request budget exhausted. Service resumes next billing cycle.",
  "resetAt": "2026-04-01T00:00:00Z"
}
```

A `Retry-After` header is set to the seconds remaining until the next month window.

### Registration Guard

`AddBudgetGuard` and `InitializeBudgetContainerAsync` both check `PersistenceSettings:UseDatabase` before
resolving `CosmosClient` from DI. When running with `FileGameRepository` (UseDatabase = false), the budget
system is skipped entirely — no crash, no orphaned registrations.

### Cosmos RU Cost

The budget flush is a single small-document upsert every 5 minutes:
- 12 writes/hour × 24 × 30 = 8,640 writes/month
- ~10 RUs per upsert = ~86,400 RUs/month total
- Less than 0.1% of the monthly RU budget

---

## Authentication

### Decision

Plain-text `AdminKey` parameter validation on every admin hub method was retired. Authentication is handled by
Microsoft Entra External ID (the CIAM successor to Azure AD B2C) with JWT bearer tokens validated server-side.
Admin access is a role claim derived from a `UserDocument` in Cosmos, not a shared secret.

### Player Identity

`Player.Id` is the B2C objectId — a persistent, provider-independent identifier that survives reconnections, session
changes, and social provider switches. Bots continue to use generated GUIDs.

### Flow

```
Client obtains JWT from Entra External ID (Google or Microsoft social login)
  ↓ JWT passed as ?access_token= query string on SignalR WebSocket connection
      ↓ JwtBearerEvents.OnMessageReceived extracts token for hub paths
          ↓ UserClaimsTransformation.TransformAsync runs on every authenticated request
              ↓ Looks up UserDocument by objectId in Cosmos
                  ↓ Creates document on first login
                  ↓ Sets IsAdmin = true if email matches EntraExternalId:AdminEmail config
                  ↓ Adds ClaimTypes.Role = "Admin" to principal if IsAdmin
```

`UserClaimsTransformation` implements `IClaimsTransformation` — ASP.NET Core calls it automatically. No explicit
invocation is needed in hub or middleware code.

### Hub Authorization

```csharp
[Authorize]                              // GameHub — any authenticated user
[Authorize(Roles = "Admin")]             // AdminHub — Admin role claim required
```

`JoinGame` no longer accepts a `playerName` parameter — display name is extracted from claims (`name` or
`ClaimTypes.Name`). `AdminHub` methods no longer have an `adminKey` parameter. `_configuration` is no longer injected
into `AdminHub`.

### SignalR Token Extraction

Browsers cannot set `Authorization` headers on WebSocket connections. The JWT is passed as `?access_token=` in the
query string and extracted in `JwtBearerEvents.OnMessageReceived` for paths `/game-hub` and `/admin-hub`.

### Entra External ID vs Entra ID

Microsoft Entra External ID is the correct product for customer-facing (CIAM) apps. Microsoft Entra ID (workforce)
requires a paid licence and is for internal users. Azure AD B2C is no longer available for new tenants as of May 2025.
App registrations must be created while the portal is confirmed inside the External ID tenant — registrations made in
the Default Directory are invisible to the correct tenant.

### Tenant Separation

Cosmos DB and compute resources are provisioned under the **default Azure directory** (where the subscription lives).
Entra External ID lives in the **Monopoly411 tenant**. These are separate concerns that do not interact at the
infrastructure level — the server talks to Cosmos for data and to Entra for token validation independently.
Managed Identity for Cosmos auth is assigned within the default directory only.

---

## Concurrency

### Problem

`GameRoomManager` is a singleton shared across every concurrent SignalR connection. Two players acting simultaneously —
both attempting to buy the same property, or a player acting while a disconnect-cleanup fires — can corrupt `GameState`
without explicit synchronisation.

### Decision — Single Lock Per Manager

All reads that feed a mutation, and all mutations themselves, are performed inside a single `Lock _lock`. The lock is
held only for the duration of the state operation, never across an `await`.

```csharp
private readonly Lock _lock = new();
private readonly Dictionary<string, GameState>  _games   = new();
private readonly Dictionary<string, GameEngine> _engines = new();

public bool MutateGame(string gameId, Action<GameState, GameEngine?> mutator)
{
    lock (_lock)
    {
        if (!_games.TryGetValue(gameId, out var game)) { return false; }
        _engines.TryGetValue(gameId, out var engine);
        mutator(game, engine);
        return true;
    }
}
```

`MutateGame` returning `false` means the game was not found — all callers in the hubs check this return value and log
a warning if it occurs. Silent swallowing of missing-game scenarios was a pre-existing issue now resolved.

**Why not `ConcurrentDictionary`:** Individual dictionary operations would be atomic, but game actions are compound —
read player state, validate, mutate board, write log. That entire sequence must be atomic as a unit, which
`ConcurrentDictionary` cannot guarantee.

**Why not per-game locks:** Trades span two players who may arrive from different hub invocations concurrently. A single
manager-level lock eliminates any lock-ordering deadlock risk and is straightforward to reason about. With an eight-player
cap, contention is negligible.

### Engine Initialisation — Atomic Create-and-Start

`StartGame` originally called `GetGameEngine` (lock), then `new GameEngine(game)` (outside lock), then `SetGameEngine`
(lock) — a check-then-act race. Two concurrent `StartGame` calls could create two engines; the second would silently
overwrite the first. Fixed with `InitializeEngine`:

```csharp
public bool InitializeEngine(string gameId, Action<GameState, GameEngine> mutator)
{
    lock (_lock)
    {
        if (!_games.TryGetValue(gameId, out var game)) { return false; }
        if (!_engines.TryGetValue(gameId, out var engine))
        {
            engine = new GameEngine(game);
            _engines[gameId] = engine;
        }
        mutator(game, engine);
        return true;
    }
}
```

Engine creation and the first mutation are now atomic within a single lock acquisition.

### Deadlock — DeleteGame Inside MutateGame

`GameCleanupService` was calling `roomManager.DeleteGame()` from inside a `MutateGame` lambda. Both methods acquire
`_lock`. .NET 9's `System.Threading.Lock` is not reentrant — this deadlocks. Fixed with a signal pattern:

```csharp
bool shouldDelete = false;

roomManager.MutateGame(gameId, (g, _) =>
{
    // ... evaluate state ...
    if (conditionMet) { shouldDelete = true; return; }
});

if (shouldDelete) { roomManager.DeleteGame(gameId); }
```

### Lock and Broadcast Pattern

Broadcasts must never happen inside the lock. The consistent pattern throughout the hub is:

```csharp
// 1. Mutate under lock via MutateGame
_rooms.MutateGame(gameId, (state, engine) => { engine?.SomeAction(); });

// 2. Persist and broadcast after lock is released
await _rooms.SaveGameAsync(gameId);
await Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
```

Holding a lock across an `await` would starve the thread pool and risk deadlock with other hub invocations waiting on
the same lock.

### Cleanup Routine

A `BackgroundService` runs on a 60-second interval and purges stale rooms. It handles abandoned games (0 players for
over 1 hour), finished games older than 7 days, and disconnected players offline for more than 10 minutes.

All mutations during cleanup are routed through `MutateGame` to guarantee they run under `_lock`, eliminating races
between the background thread and concurrent hub invocations. `VacuumStorageAsync(predicate)` fans out repository
deletes via `Task.WhenAll` after snapshotting candidate IDs under the lock.

---

## SignalR Hub Architecture

### Design — Thin Hub, Three-Step Pattern

`GameHub` is a transport boundary only. It contains no game logic. Every hub method follows the same three steps:

```
1. Validate  — is the caller permitted to do this right now?
2. Delegate  — pass to GameRoomManager or TradeService
3. Broadcast — push updated GameStateDto to the SignalR group
```

**Invariant:** The hub never calls `GameEngine` directly. All mutations go through `MutateGame`, `ExecuteWithEngine`,
or `InitializeEngine` on `GameRoomManager`, which own `_lock`. The engine is always invoked from within a manager
method that already holds the lock.

### Hub Error Logging

All hub error paths go through a `SendError` helper that logs a structured warning before sending the error to the
client. Previously, errors were sent to the caller with nothing written server-side.

```csharp
private async Task SendError(string method, string gameId, string message)
{
    _logger.LogWarning("[{Method}] {Message} — gameId={GameId} connectionId={ConnectionId}",
        method, message, gameId, Context.ConnectionId);
    await Clients.Caller.SendAsync("Error", message);
}
```

### Caller Identity

Hub methods no longer resolve the calling player by `Context.ConnectionId`. They resolve by B2C objectId extracted from
`Context.User` claims:

```csharp
private string? GetCallerObjectId() =>
    Context.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    ?? Context.User?.FindFirst("oid")?.Value;
```

### Hub Groups

Each game is a SignalR group keyed by `gameId`. Players join the group on `JoinGame` and leave on `LeaveGame` or
disconnect. `OnDisconnectedAsync` finds the player's game by objectId via `GetGameByPlayerId` and marks them
disconnected via `MarkPlayerDisconnected` — which runs under `_lock` without holding it across any `await`.

### Hub Methods

**Room management:**

| Method                         | Broadcasts                                  |
|--------------------------------|---------------------------------------------|
| `CreateGame(playerName)`       | `GameCreated` → creator                     |
| `JoinGame(gameId)`             | `PlayerJoined` → room                       |
| `LeaveGame(gameId)`            | `PlayerLeft` / `HostChanged` → room         |
| `StartGame(gameId)` *(host)*   | `GameStarted` → room                        |
| `GetAvailableGames()`          | Returns `List<GameRoomInfo>` to caller only |
| `GetGameLobby(gameId)`         | Returns `GameRoomInfo` to caller only       |

**Gameplay:**

| Method                                    | Broadcasts                                       |
|-------------------------------------------|--------------------------------------------------|
| `RollDice(gameId)`                        | `GameStateUpdated` + `DiceRolled(d1, d2)` → room |
| `BuyProperty(gameId)`                     | `GameStateUpdated` → room                        |
| `BuildHouse(gameId, propertyId)`          | `GameStateUpdated` → room                        |
| `BuildHotel(gameId, propertyId)`          | `GameStateUpdated` → room                        |
| `MortgageProperty(gameId, propertyId)`    | `GameStateUpdated` → room                        |
| `UnmortgageProperty(gameId, propertyId)`  | `GameStateUpdated` → room                        |
| `EndTurn(gameId)`                         | `GameStateUpdated` → room                        |
| `HandleJail(gameId, action)`              | `GameStateUpdated` → room                        |
| `ResignPlayer(gameId)`                    | `GameStateUpdated` → room                        |
| `ProposeTrade(gameId, toId, offer)`       | `TradeProposed` → recipient only                 |
| `RespondToTrade(gameId, tradeId, accept)` | `GameStateUpdated` → room                        |

**Admin** *(requires Admin role):*

| Method                      | Broadcasts                                |
|-----------------------------|-------------------------------------------|
| `PauseGame(gameId)`         | `GamePaused` → room                       |
| `ResumeGame(gameId)`        | `GameResumed` → room                      |
| `KickPlayer(gameId, id)`    | `PlayerKicked` → room · `Kicked` → target |
| `ForceEndGame(gameId)`      | `GameForceEnded` → room                   |
| `AddBotToGame(gameId, n)`   | `GameStateUpdated` → room                 |
| `GetGameDetails(gameId)`    | `GameDetails` → caller only               |

### Hub Events — Server to Client

| Event              | Payload                             | Recipients          |
|--------------------|-------------------------------------|---------------------|
| `GameCreated`      | `gameId`                            | Creator             |
| `PlayerJoined`     | `playerId, playerName, playerCount` | Room                |
| `PlayerLeft`       | `playerCount`                       | Room                |
| `HostChanged`      | `newHostName`                       | Room                |
| `GameStarted`      | `currentPlayer, turn`               | Room                |
| `GameStateUpdated` | `GameStateDto`                      | Room                |
| `DiceRolled`       | `d1, d2`                            | Room                |
| `CardDrawn`        | `CardDto`                           | Room                |
| `TradeProposed`    | `TradeOfferDto`                     | Recipient only      |
| `GamePaused`       | —                                   | Room                |
| `GameResumed`      | —                                   | Room                |
| `TurnWarning`      | `message`                           | Current player      |
| `PlayerKicked`     | `playerName`                        | Room                |
| `Kicked`           | `message`                           | Target only         |
| `GameForceEnded`   | —                                   | Room                |
| `Reconnected`      | —                                   | Reconnecting client |

---

## React Client Architecture

### Serving Strategy

`index.html` is the entire static shell. It loads the React bundles as plain `<script>` tags and exposes the SignalR hub
URL as a `window` constant. After the initial load, all UI is React — the server renders nothing further.

No npm build pipeline, no module bundler. Scripts are loaded in dependency order via `<script>` tags; `globals.js`
aliases React hooks onto `window` so every file can access them without import syntax.

### Component Tree

```
App
├── Header
├── HomePage                ← game list, create / join
├── LobbyPage               ← waiting room, player list, start button
└── GamePage                ← full game UI; owns all hub subscriptions
    ├── Board               ← 40-cell grid, scaled tokens, inline dice + action panel
    │   ├── DiceTray        ← die faces, sum label, reserved doubles slot
    │   └── InspectModal    ← space detail popup on click
    ├── PlayerPanel         ← cash, position, status per player (left sidebar)
    ├── PropertiesPanel     ← owned properties, build / mortgage actions (right)
    └── EventLog            ← togglable far-right panel
```

### Hub Connection — Single Shared Instance

One `gameHub` object is created at app startup and reused for the page lifetime. It wraps `HubConnection`, exposes `on`
and `call`, and reconnects automatically. The JWT is appended as `?access_token=` on the connection URL.

```js
const gameHub = (() => {
    const conn = new signalR.HubConnectionBuilder()
        .withUrl(window.HUB_URL + "?access_token=" + getToken())
        .withAutomaticReconnect()
        .build();

    return {
        start: () => conn.start(),
        on(event, handler) { conn.on(event, handler); return () => conn.off(event, handler); },
        call(method, ...args) { return conn.invoke(method, ...args); },
    };
})();
```

### Event Subscription Pattern

Components subscribe inside `useEffect` and return each unsubscribe function as cleanup. This guarantees handlers are
torn down when the component unmounts and prevents duplicate listener accumulation across page transitions or StrictMode
double-invocations.

```js
useEffect(() => {
    const unsubs = [
        gameHub.on('GameStateUpdated', state           => setGameState(state)),
        gameHub.on('DiceRolled',       (d1, d2)        => settleDice([d1, d2])),
        gameHub.on('CardDrawn',        card             => setDrawnCard(card)),
        gameHub.on('TradeProposed',    offer            => setIncomingTrade(offer)),
        gameHub.on('GamePaused',       ()               => setPaused(true)),
        gameHub.on('GameResumed',      ()               => setPaused(false)),
        gameHub.on('TurnWarning',      ({ message })    => toast(`⏰ ${message}`, 'warning')),
        gameHub.on('PlayerKicked',     ({ playerName }) => toast(`${playerName} was removed`)),
        gameHub.on('GameForceEnded',   ()               => { toast('Game ended', 'error'); onLeave(); }),
        gameHub.on('Kicked',           msg              => { toast(msg, 'error'); onLeave(); }),
        gameHub.on('Reconnected',      ()               => gameHub.call('JoinGame', gameId)),
    ];

    return () => unsubs.forEach(fn => fn());
}, [gameId]);
```

Note: `JoinGame` no longer receives `playerName` — the server derives it from the authenticated claims.

### State Flow — Server is Source of Truth

The client never optimistically mutates game state. It updates only after receiving `GameStateUpdated`. Local state is
purely presentational: dice animation phase, open modals, selected trade target.

```
User clicks Roll
  → gameHub.call('RollDice', gameId)
      → Hub validates turn via objectId claim, delegates to engine via MutateGame
          → Hub broadcasts GameStateUpdated + DiceRolled to group
              → settleDice([d1, d2])  starts / queues animation
              → setGameState(dto)     triggers React re-render
```

`settleDice` is provided by `useDiceRoll()` in `animation.js`. If the server responds before the shuffle animation
completes, the real values are held in a ref and applied the moment the animation settles — preventing the dice from
snapping before the visual completes.

---

## Services

### GameRoomManager

Thread-safe singleton. Single source of truth for all active game instances.

| Method                              | Description                                                          |
|-------------------------------------|----------------------------------------------------------------------|
| `CreateGame()`                      | Generate 8-char ID, create `GameState`, fire background save         |
| `GetGame(gameId)`                   | Fetch `GameState` under lock                                         |
| `GetGameEngine(gameId)`             | Fetch engine for active game                                         |
| `SetGameEngine(gameId, engine)`     | Store engine when game starts                                        |
| `MutateGame(gameId, mutator)`       | Mutate game state and engine atomically under `_lock`; returns false if not found |
| `ExecuteWithEngine(gameId, action)` | Mutate when engine presence is required; delegates to `MutateGame`   |
| `InitializeEngine(gameId, mutator)` | Creates engine atomically if absent, then runs mutator under lock    |
| `GetGameByPlayerId(playerId)`       | Find game for disconnect handling                                    |
| `MarkPlayerDisconnected(playerId)`  | Sets IsConnected=false, DisconnectedAt under lock — no async I/O    |
| `DeleteGame(gameId)`                | Remove from memory under lock; fires background repository delete    |
| `VacuumStorageAsync(predicate)`     | Bulk-remove games; snapshots IDs under lock, deletes fan out async  |
| `SaveGameAsync(gameId)`             | Snapshot under lock, persist outside lock via IGameRepository        |
| `GetStats()`                        | Diagnostics — total, in-progress, waiting, player counts             |

### TradeService

Decoupled from SignalR entirely — no hub references, independently testable. Wraps engine trade calls with
pre-validation and structured logging. All mutations route through `MutateGame`.

| Method                                        | Returns                                |
|-----------------------------------------------|----------------------------------------|
| `ProposeTrade(gameId, fromId, toId, offer)`   | `TradeOffer?`                          |
| `AcceptTrade(gameId, tradeId, playerId)`      | `bool`                                 |
| `RejectTrade(gameId, tradeId, playerId)`      | `bool`                                 |
| `CancelTrade(gameId, tradeId, playerId)`      | `bool`                                 |
| `GetPendingTrades(gameId)`                    | `List<TradeOffer>`                     |
| `GetPendingTradesForPlayer(gameId, playerId)` | Trades awaiting this player's response |

### LobbyService

Handles all pre-game room lifecycle operations, decoupled from the hub transport layer.

| Method                                 | Description                               |
|----------------------------------------|-------------------------------------------|
| `CreateRoom(playerName, connId)`       | Initialise room, assign host              |
| `JoinRoom(gameId, playerName, connId)` | Validate and add player to waiting room   |
| `LeaveRoom(gameId, playerId)`          | Remove player; promote new host if needed |
| `GetAvailableRooms()`                  | Returns joinable rooms                    |

### TurnTimerService

Manages per-game countdown timers. Fires `TurnWarning` to the current player before auto-advancing the turn on expiry.

### BudgetGuardService

Background service tracking Azure Container Apps free-tier consumption. See [Budget Guard](#budget-guard) section for
full architecture. Registered as both a singleton (for direct method calls from middleware and hubs) and a hosted
service (for `ExecuteAsync` background flush loop).

### Registration (Program.cs)

```csharp
builder.Services.AddGamePersistence(builder.Configuration);   // IGameRepository + CosmosClient
builder.Services.AddGameAuth(builder.Configuration);          // JWT, Entra, IUserRepository
builder.Services.AddBudgetGuard(builder.Configuration);       // BudgetGuardService (no-op if UseDatabase=false)
builder.Services.AddGameServices();                           // GameRoomManager, GameCleanupService

builder.Services.AddSingleton<InputValidator>();
builder.Services.AddSingleton<TradeService>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<BotDecisionEngine>();
builder.Services.AddSingleton<BotTurnOrchestrator>();
builder.Services.AddHostedService<TurnTimerService>();

app.UseMiddleware<BudgetMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<GameHub>("/game-hub");
app.MapHub<AdminHub>("/admin-hub");

await app.InitializeBudgetContainerAsync();   // ensures budget container exists
await app.InitializeGameManagerAsync();        // must precede RunAsync
await app.RunAsync();
```

---

## Data Transfer Objects

All SignalR serialisation uses strongly-typed DTOs. No anonymous types, no `dynamic`.

**`GameStateDto`** — `GameId` · `Status` · `Turn` · `CurrentPlayer` · `Players[]` · `Board[]` · `EventLog` (last 20
entries) · `CurrentTurnStartedAt`

**`PlayerDto`** — `Id` · `Name` · `Cash` · `Position` · `IsInJail` · `IsBankrupt` · `KeptCardCount` · `HasRolledDice` ·
`IsConnected` · `IsBot`

**`PropertyDto`** — `Id` · `Name` · `Type` · `Position` · `OwnerId` · `HouseCount` · `HasHotel` · `IsMortgaged` ·
`ColorGroup` · `PurchasePrice`

**`TradeOfferDto`** — `Id` · `FromPlayerId` · `FromPlayerName` · `ToPlayerId` · `OfferedPropertyIds[]` · `OfferedCash` ·
`RequestedPropertyIds[]` · `RequestedCash` · `Status` · `CreatedAt`

**`GameRoomInfo`** — `GameId` · `HostId` · `PlayerCount` · `MaxPlayers` · `Players[]` · `CreatedAt`

**`PlayerLobbyInfo`** — Lightweight player projection used in lobby broadcasts before game start

**`ServerHealthStatsDto`** — Diagnostics payload served by `AdminHub`, includes `BudgetSnapshot`

**`CardDto`** — `Type` · `Text` · `Amount?`

---

## Game Lifecycle

**Waiting** — Host creates room → players join via `JoinGame` → `PlayerJoined` broadcast to room → only host can start.

**In Progress** — `StartGame` validates 2+ players → engine created atomically via `InitializeEngine` → status → `InProgress` → `GameStarted`
broadcast → no further joins allowed.

**Finished** — Engine detects last standing player → `EndGame(winner)` → status → `Finished` → cleanup routine removes
room after 30 s.

**Leaving during lobby** — Player removed → if host left, `player[0]` promoted → `HostChanged` broadcast. Last player
leaves → room deleted immediately.

**Disconnect mid-game** — `OnDisconnectedAsync` finds game by objectId → `MarkPlayerDisconnected` runs under lock →
`GameStateUpdated` broadcast to remaining players. Reconnect window applies before cleanup evicts the player.

**Reconnect** — `withAutomaticReconnect()` restores transport → client's `Reconnected` handler calls `JoinGame` →
hub re-adds connection to the group, updates `ConnectionId` on the player record, and broadcasts current `GameStateDto`.

---

## Edge Cases

| Scenario                        | Behaviour                                                                |
|---------------------------------|--------------------------------------------------------------------------|
| Player joins full game          | Error: "Game is full"                                                    |
| Start with fewer than 2 players | Error: "Need at least 2 players"                                         |
| Non-host calls `StartGame`      | Error: "Only host can start"                                             |
| Host leaves lobby               | `player[0]` promoted, `HostChanged` broadcast                            |
| Last player leaves              | Room deleted immediately                                                 |
| Player disconnects mid-turn     | Marked disconnected, state broadcast to room                             |
| Reconnect to active game        | Full `GameStateDto` sent to reconnecting connection                      |
| Buy already-owned property      | Engine rejects: "Already owned"                                          |
| Insufficient cash               | Engine rejects with reason, no state change                              |
| Mortgage with houses present    | Engine rejects in `MortgageProperty()`                                   |
| Jail — roll doubles             | Engine releases immediately in `ReleaseFromJail()`                       |
| Trade for unowned property      | `TradeService.ValidateTradeAssets()` rejects                             |
| "Go back 3" lands on Chance     | Chain draw: next card executed immediately                               |
| Player bankrupted mid-trade     | Trade cancelled, properties returned to bank                             |
| Concurrent buy attempts         | `_lock` ensures first writer wins; second receives rejection             |
| Unauthenticated hub connection  | ASP.NET Core rejects at middleware — hub never invoked                   |
| Non-admin calls admin method    | `[Authorize(Roles="Admin")]` rejects at hub class level                  |
| MutateGame returns false        | Game vanished between pre-check and mutation — logged as warning         |
| HTTP budget exhausted           | `BudgetMiddleware` returns 503 with `Retry-After` header                 |
| Connection limit reached        | `TryAddConnection()` returns false, SignalR connection refused           |
| Game limit reached              | `TryAddGame()` returns false, `CreateGame` rejects                       |
| Container restart               | `BudgetGuardService` reloads consumption counters from Cosmos on startup |
| UseDatabase = false             | `AddBudgetGuard` and `InitializeBudgetContainerAsync` no-op cleanly      |

---

## Intentional Exclusions

**No auctions by design** — Property stays unowned if declined. Auctions prolong games and were removed after feedback.

**No Newtonsoft.Json** — The entire stack uses System.Text.Json including Cosmos DB via `StjCosmosSerializer`.

**No per-game locks** — A single manager-level lock is simpler to reason about and sufficient at the player cap.

**No budget persistence to local file** — Budget counters are stored in Cosmos. Using the file system would lose
counters on container restart. The periodic flush pattern (5-minute interval, reload on startup) is the correct
enterprise pattern: in-memory hot path, durable cold path, acceptable ~5-minute under-count window on unclean restart.

**No autoscale throughput on Cosmos** — Manual provisioning at 990 RU/s keeps the account within the free tier
ceiling. Autoscale could silently breach the 1,000 RU/s threshold and incur charges.

---

## Architecture Invariants

| Layer           | Responsibility                                                          |
|-----------------|-------------------------------------------------------------------------|
| **Models**      | Data containers — no logic                                              |
| **Engine**      | All game rules — stateless, validated, logged                           |
| **Services**    | Orchestration, locking, cleanup                                         |
| **Hub**         | Transport only — validate · delegate · broadcast                        |
| **DTOs**        | Typed serialisation boundary                                            |
| **Persistence** | `IGameRepository` / `IUserRepository` — swappable, isolated from engine |
| **Auth**        | Entra External ID JWT — identity from claims, roles from Cosmos         |
| **Budget**      | In-memory counters, lock-free hot path, periodic Cosmos flush           |
| **React**       | Presentational — server is the single source of truth                   |

- Hub never calls `GameEngine` directly — always via `MutateGame`, `ExecuteWithEngine`, or `InitializeEngine`
- State mutations only happen inside `_lock`
- Broadcasts and async I/O happen after the lock is released, never inside it
- `DeleteGame` is never called from inside a `MutateGame` lambda — use a signal flag instead
- `InitializeAsync` is awaited before `RunAsync` — hosted services never run against an empty game dictionary
- `BudgetGuardService` is only registered when `PersistenceSettings:UseDatabase` is true — no orphaned DI registrations
- `BudgetMiddleware` is registered before `UseRouting` so it covers all endpoints including hubs
- React components always return their unsubscribe functions from `useEffect`
- Client never mutates game state locally — waits for `GameStateUpdated`
- Player identity is always resolved from B2C objectId claims, never from `ConnectionId`
- Admin access is a Cosmos-persisted role claim, never a shared secret in config or a method parameter
- Cosmos throughput is provisioned manually — autoscale is never used, free tier ceiling must not be breached