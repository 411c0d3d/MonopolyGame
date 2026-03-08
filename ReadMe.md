# Monopoly Online

## [Play Now](https://monopoly-game.jollyfield-95a61d31.germanywestcentral.azurecontainerapps.io/) v1.0.0

A full-featured multiplayer Monopoly built on ASP.NET Core 10 with a SignalR hub and a React client served through a
static HTML shell. Complete game engine covering all classic rules — card system, trading, jail, rent, and buildings.
Active game state is held in memory for performance and persistence is swappable between repository implementations —
either Azure Cosmos DB for production or JSON files for local development via app settings. Authentication is handled
by Microsoft Entra External ID with social login (Google, Microsoft). Admin access is enforced by a role claim. A
budget guard service enforces Azure Container Apps consumption limits and persists monthly counters to Cosmos.

See [ADR.md](ADR.md) for the full architecture decision record covering all major design choices and trade-offs.

## Preview

![GamePlay](GamePlay1.gif)

![GamePlay](GamePlay2.gif)

---

## Features

- **Real-time multiplayer** — SignalR keeps every client in sync with zero polling
- **Complete ruleset** — all 40 spaces, 32 cards (Chance + Community Chest), rent tables, monopoly bonuses, railroads, utilities, and jail mechanics
- **Social login** — sign in with Google or Microsoft via Microsoft Entra External ID; no username/password to manage
- **Trading system** — propose, counter, accept, or reject trades with properties and cash
- **Buildings** — buy houses and hotels per color group with full monopoly validation
- **Mortgage system** — mortgage and unmortgage properties for liquidity
- **Self-managed lobbies** — create a room, share the code, start when ready; host transfers automatically if the host leaves
- **Bot support** — fill empty seats with AI players
- **Turn timer** — auto-advance if a player idles too long
- **Event log** — toggleable live feed of every game action
- **Reconnect recovery** — drop and rejoin mid-game; full state is restored via persistent identity
- **Budget guard** — enforces Azure Container Apps limits; returns 503 with `Retry-After` before any overage is incurred

---

## Tech Stack

| Layer               | Technology                                                                                                                                |
|---------------------|-------------------------------------------------------------------------------------------------------------------------------------------|
| Server              | ASP.NET Core 10                                                                                                                           |
| Real-time transport | SignalR                                                                                                                                   |
| Client              | React (no bundler — plain script tags via index.html)                                                                                     |
| Auth                | Microsoft Entra External ID (CIAM) — Google + Microsoft social login, JWT bearer                                                          |
| State               | In-memory (`Dictionary<string, GameState>`) with swappable `IGameRepository` — JSON files locally, Azure Cosmos DB in production          |
| Database            | Azure Cosmos DB (NoSQL API, v3 SDK) — `games`, `users`, and `budget` containers                                                           |
| Serialisation       | Strongly-typed DTOs, System.Text.Json throughout (including Cosmos via custom `StjCosmosSerializer`)                                      |
| Budget enforcement  | `BudgetGuardService` — lock-free in-memory counters, periodic Cosmos flush, monthly reset                                                 |

---

## Getting Started

**Requirements:** .NET 10 SDK

```bash
git clone https://github.com/411c0d3d/MonopolyGame
cd MonopolyGame
dotnet run --project MonopolyServer
```

Local testing without auth or Cosmos: set `PersistenceSettings:UseDatabase` to `false` in
`appsettings.Development.json` — the server runs with file persistence and no external dependencies.

### Local Setup with Auth and Cosmos

Requires a [Microsoft Entra External ID](https://learn.microsoft.com/en-us/entra/external-id/) tenant and an Azure
Cosmos DB account (or the [Cosmos emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/local-emulator)).

Store credentials via User Secrets — never commit them:

```bash
cd MonopolyServer
dotnet user-secrets set "EntraExternalId:Authority"     "https://<tenant>.ciamlogin.com/<tenantId>/v2.0"
dotnet user-secrets set "EntraExternalId:ClientId"      "<app-registration-client-id>"
dotnet user-secrets set "EntraExternalId:ClientSecret"  "<client-secret>"
dotnet user-secrets set "EntraExternalId:Domain"        "<tenant>.onmicrosoft.com"
dotnet user-secrets set "EntraExternalId:AdminEmail"    "<your-email>"
dotnet user-secrets set "CosmosDb:Endpoint"             "https://localhost:8081"
dotnet user-secrets set "CosmosDb:AuthKey"              "<auth-key>"
dotnet user-secrets set "CosmosDb:DatabaseId"           "MonopolyDb"
dotnet user-secrets set "CosmosDb:CollectionId"         "games"
dotnet user-secrets set "CosmosDb:UsersCollectionId"    "users"
```

Then open `http://localhost:5299` in your browser.

### Production Setup (Azure Cosmos DB)

Swap the emulator endpoint and key for your real Azure Cosmos DB account:

```bash
dotnet user-secrets set "CosmosDb:Endpoint" "https://<your-account>.documents.azure.com:443/"
dotnet user-secrets set "CosmosDb:AuthKey"  "<your-primary-key>"
```

The server detects whether the endpoint is `localhost` and automatically applies the appropriate connection mode
(`Gateway` + cert bypass for the emulator, `Direct` for Azure). No code changes or environment flags are needed.

The `CosmosGameRepository` will create `MonopolyDb` and all three containers (`games`, `users`, `budget`) on startup
if they do not already exist.

---

## Project Structure

### Server — `MonopolyServer/`

```
MonopolyServer/
├── Bot/
│   ├── BotDecisionEngine.cs         # Bot move evaluation and decision logic
│   └── BotTurnOrchestrator.cs       # Orchestrates full bot turn execution
├── Data/
│   └── Repositories/
│       ├── GameStorage/
│       │   ├── CosmosGameRepository.cs             # Cosmos DB game persistence (v3 SDK)
│       │   ├── FileGameRepository.cs               # File system game persistence
│       │   ├── GameDocument.cs                     # Cosmos document wrapper for GameState
│       │   ├── IGameRepository.cs                  # Game persistence contract
│       │   ├── PersistenceServiceExtensions.cs     # DI wiring — persistence and game services
│       │   └── StjCosmosSerializer.cs              # STJ-backed CosmosSerializer
│       └── UserAuth/
│           ├── AuthServiceExtensions.cs            # DI wiring — auth, JWT, authorization
│           ├── CosmosUserRepository.cs             # Cosmos user persistence
│           ├── IUserRepository.cs                  # User persistence contract
│           └── UserClaimsTransformation.cs         # Enriches principal with Admin role from Cosmos
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
│   │   ├── AzureAdSettings.cs       # Typed config for Microsoft Entra External ID
│   │   └── UserDocument.cs          # Cosmos user document (id = Microsoft Entra External ID as objectId)
│   ├── Budget/
│   │   ├── BudgetDocument.cs        # Cosmos document tracking monthly resource consumption
│   │   ├── BudgetGuardService.cs    # BackgroundService — in-memory counters, periodic Cosmos flush
│   │   ├── BudgetMiddleware.cs      # HTTP middleware — enforces monthly request budget
│   │   ├── BudgetServiceExtensions.cs  # DI wiring — budget guard registration
│   │   └── BudgetSnapshot.cs        # Read-only consumption snapshot for admin panel
│   ├── CosmosSettings.cs            # Typed Cosmos connection config
│   ├── GameCleanupService.cs        # BackgroundService — purges stale rooms every 60 s
│   ├── GameStateMapper.cs           # Maps GameState → GameStateDto
│   ├── InputValidator.cs            # Shared input guard helpers
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
│   │   ├── modals.js                # Reusable modal components — TradeOfferModal, GameOverModal, AdminKickModal
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
│   │   ├── lobby_page.js            # Pre-game lobby — player list, start button
│   │   └── login_page.js            # Login page — redirects to Microsoft Entra External ID for auth
│   ├── utils/
│   │   ├── auth_service.cs          # Auth helpers — Login/logout handler, parse JWT, manage session, attach auth header to fetch
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

## Architecture

See [ADR.md](ADR.md) for the full architecture decision record covering:

- **Concurrency model** — how `GameRoomManager` uses a single `_lock` to make compound game operations atomic via `MutateGame`, `ExecuteWithEngine`, and `InitializeEngine`, and why `ConcurrentDictionary` alone is insufficient
- **Authentication** — Microsoft Entra External ID JWT validation, `UserClaimsTransformation`, and how Admin role is derived from Cosmos rather than a shared secret
- **Persistence layer** — `IGameRepository` / `IUserRepository` swappable backends, shared `CosmosClient`, `StjCosmosSerializer`, and environment-aware connection mode switching
- **Azure Cosmos DB setup** — account configuration, container layout, RU/s consumption provisioning rationale, capacity and scale estimates
- **Budget guard** — lock-free in-memory counters, periodic Cosmos flush, monthly reset, enforcement points, and RU cost analysis
- **SignalR hub design** — thin hub pattern (validate · delegate · broadcast), group management, and the disconnect/reconnect lifecycle
- **React architecture** — how the shell bootstraps React, the component tree, event subscription pattern, and why there is no build pipeline
- **Cleanup routine** — `GameCleanupService` background timer, `VacuumStorageAsync(predicate)`, and how all cleanup mutations route through `MutateGame`
- **Startup ordering** — why `InitializeBudgetContainerAsync` and `InitializeAsync` are awaited before `RunAsync` and the race condition this prevents

---

## How a Turn Works

```
Player clicks Roll
  → gameHub.call('RollDice', gameId)
      → Hub resolves caller identity from Microsoft Entra External ID claim
          → GameRoomManager.ExecuteWithEngine delegates to GameEngine.RollDice under lock
              → Hub broadcasts GameStateUpdated + DiceRolled to all players in group
                  → React re-renders board, tokens, and cash
                  → Dice animation plays and settles on real values
                  → Player takes further actions (buy property, build, trade)
                  → Each action validated server-side and broadcast as GameStateUpdated
```

The server is always the source of truth. The client never modifies game state locally — it waits for `GameStateUpdated`
before re-rendering.

---

## Data Layer

Active game state is held in memory (`Dictionary<string, GameState>`) for low-latency reads and writes during play,
and persisted via `IGameRepository` for recovery across restarts.

`PersistenceSettings:UseDatabase` in configuration selects the active backend:

- **`false`** — `FileGameRepository` (JSON files, no external dependencies, default for local dev)
- **`true`** — `CosmosGameRepository` (Azure Cosmos DB NoSQL API, v3 SDK, production)

Switching backends requires no changes to engine or hub code. The persistence contract is fully isolated from game logic.

A separate `users` container stores `UserDocument` records keyed by Microsoft Entra External ID, created on first login and used to
derive the Admin role. A `budget` container stores the single `BudgetDocument` tracking monthly cloud resources consumption.

### Budget Guard

`BudgetGuardService` enforces Azure Container Apps resources consumption before any cost is incurred:

| Resource               | Base tier | Cap (90%) |
|------------------------|-----------|-----------|
| vCPU-seconds / month   | 180,000   | 162,000   |
| HTTP requests / month  | 2,000,000 | 1,800,000 |
| Concurrent games       | —         | 10        |
| Concurrent connections | —         | 80        |

All hot-path counting is lock-free (`Interlocked`). State is flushed to Cosmos every 5 minutes and reloaded on
container restart so counters survive redeploys. When a limit is reached, the server returns `503 Service Unavailable`
with a `Retry-After` header pointing to the next monthly reset. The budget system is skipped entirely when
`PersistenceSettings:UseDatabase` is `false`.

---

## Non-Features (Intentional)

- **No auctions** — property stays unowned if declined; auctions slow games down
- **No Newtonsoft.Json** — the entire stack uses System.Text.Json including Cosmos serialization
- **No autoscale throughput** — Cosmos is provisioned at manual 1K RU/s to stay within the consumption ceiling
- **No budget file persistence** — counters live in Cosmos so they survive container restarts; a local file would reset on every redeploy
- **Admin access via role claim, not a shared key** — the Admin hub requires the Admin role on the authenticated principal; there is no `AdminKey` parameter or configuration value

---

## License

MIT