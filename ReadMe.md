# Monopoly Online

A full-featured multiplayer Monopoly with ASP.NET Core 10 with a SignalR hub and a React client served through a static HTML shell.
Complete game engine covering all classic rules — card system, trading, jail, rent, and buildings. Active game state is
held in memory for performance and persistance is swappable between repository layer with — either Azure Cosmos DB for production or JSON files for local
development via App settings. Authentication is handled by Microsoft Entra External ID with social
login (Google, Microsoft). Admin access is enforced by a role claim.

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

---

## Tech Stack

| Layer               | Technology                                                                                                                                 |
|---------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| Server              | ASP.NET Core 10                                                                                                                            |
| Real-time transport | SignalR                                                                                                                                    |
| Client              | React + Razor (no bundler — plain script tags via index.html)                                                                              |
| Auth                | Microsoft Entra External ID (CIAM) — Google + Microsoft social login, JWT bearer                                                          |
| State               | In-memory (`Dictionary<string, GameState>`) with swappable `IGameRepository` — JSON files locally, Azure Cosmos DB in production          |
| Database            | Azure Cosmos DB (SQL API, v3 SDK) — games container + users container                                                                     |
| Serialisation       | Strongly-typed DTOs, System.Text.Json throughout (including Cosmos via custom `StjCosmosSerializer`)                                       |

---

## Getting Started

**Requirements:** .NET 10 SDK

```bash
git clone https://github.com/411c0d3d/MonopolyGame
cd MonopolyGame
dotnet run --project MonopolyServer
```

Local testing without auth: set `PersistenceSettings:UseDatabase` to `false` in `appsettings.Development.json` — the server runs with file persistence and no Cosmos dependency.

### Local Setup with Auth and Cosmos

Requires a [Microsoft Entra External ID](https://learn.microsoft.com/en-us/entra/external-id/) tenant and an Azure Cosmos DB account (or the [Cosmos emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/local-emulator)).

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
│           ├── AuthServiceExtensions.cs     # DI wiring — auth, JWT, authorization
│           ├── CosmosUserRepository.cs      # Cosmos user persistence
│           ├── IUserRepository.cs           # User persistence contract
│           └── UserClaimsTransformation.cs  # Enriches principal with Admin role from Cosmos
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
│   │   └── UserDocument.cs          # Cosmos user document (id = B2C objectId)
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

## Architecture

See [ADR.md](ADR.md) for the full architecture decision record covering:

- **Concurrency model** — how `GameRoomManager` uses a single `_lock` to make compound game operations atomic via `MutateGame`, `ExecuteWithEngine`, and `InitializeEngine`, and why `ConcurrentDictionary` alone is insufficient
- **Authentication** — Microsoft Entra External ID JWT validation, `UserClaimsTransformation`, and how Admin role is derived from Cosmos rather than a shared secret
- **Persistence layer** — `IGameRepository` / `IUserRepository` swappable backends, shared `CosmosClient`, and the `StjCosmosSerializer` that eliminated double-serialization
- **SignalR hub design** — thin hub pattern (validate · delegate · broadcast), group management, and the disconnect/reconnect lifecycle
- **React + Razor architecture** — how the shell bootstraps React, the component tree, and why there is no build pipeline
- **Event subscription pattern** — how components subscribe in `useEffect` and always return unsubscribe functions to prevent listener leaks
- **Cleanup routine** — the `GameCleanupService` background timer, `VacuumStorageAsync(predicate)`, and how all cleanup mutations route through `MutateGame`
- **Startup ordering** — why `InitializeAsync` is awaited before `RunAsync` and the race condition this prevents

---

## How a Turn Works

```
Player clicks Roll
  → gameHub.call('RollDice', gameId)
      → Hub resolves caller identity from B2C objectId claim
          → GameRoomManager.ExecuteWithEngine delegates to GameEngine.RollDice under lock
              → Hub broadcasts GameStateUpdated + DiceRolled to all players in group
                  → React re-renders board, tokens, and cash
                  → Dice animation plays and settles on real values
                  → Player takes further actions (buy property, build, trade)
                  → Each action validated server-side and broadcast as GameStateUpdated
```

The server is always the source of truth. The client never modifies game state locally — it waits for `GameStateUpdated` before re-rendering.

---

## Data Layer

Active game state is held in both memory (`Dictionary<string, GameState>`) for low-latency reads and writes during play
and persisted via `IGameRepository` for recovery across restarts.

`PersistenceSettings:UseDatabase` in configuration selects the active backend:

- **`false`** — `FileGameRepository` (JSON files, no external dependencies, default for local dev)
- **`true`** — `CosmosGameRepository` (Azure Cosmos DB SQL API, v3 SDK, production)

Switching backends requires no changes to engine or hub code. The persistence contract is fully isolated from game logic.

A separate `users` container in Cosmos stores `UserDocument` records keyed by B2C objectId, created on first login and used to derive the Admin role.

---

## Non-Features (Intentional)

- **No auctions by design** — property stays unowned if declined; auctions slow games down
- **No Newtonsoft.Json** — the entire stack uses System.Text.Json including Cosmos serialization
- **Admin access via role claim, not a shared key** — the Admin hub requires the Admin role on the authenticated principal; there is no `AdminKey` parameter or configuration value

---

## License

MIT