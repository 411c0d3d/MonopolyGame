# Monopoly Online

A full-featured multiplayer Monopoly game built with ASP.NET Core 10, SignalR, and React — playable in the browser with up to four players per room. Complete classic ruleset including cards, trading, buildings, jail, and rent.

![GamePlay](GamePlay.gif)

---

## Features

- **Real-time multiplayer** — SignalR keeps every client in sync with zero polling
- **Complete ruleset** — all 40 spaces, 32 cards (Chance + Community Chest), rent tables, monopoly bonuses, railroads, utilities, and jail mechanics
- **Trading system** — propose, counter, accept, or reject trades with properties and cash
- **Buildings** — buy houses and hotels per color group with full monopoly validation
- **Mortgage system** — mortgage and unmortgage properties for liquidity
- **Self-managed lobbies** — create a room, share the code, start when ready; host transfers automatically if the host leaves
- **Bot support** — fill empty seats with AI players
- **Turn timer** — auto-advance if a player idles too long
- **Event log** — toggleable live feed of every game action
- **Reconnect recovery** — drop and rejoin mid-game; full state is restored

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Server | ASP.NET Core 10 |
| Real-time transport | SignalR |
| Client | React (no bundler — plain script tags via index.html) |
| State | In-memory (`Dictionary<string, GameState>`) with JSON file persistence for recovery |
| Serialisation | Strongly-typed DTOs, System.Text.Json |

---

## Getting Started

**Requirements:** .NET 10 SDK

```bash
git clone https://github.com/your-org/monopoly-online
cd monopoly-online
dotnet run --project MonopolyServer
```

Open `http://localhost:5000` in your browser. Create a room, share the URL with friends, and start the game when everyone has joined.

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
│   ├── AdminHub.cs                  # Admin diagnostics and server health
│   └── GameHub.cs                   # SignalR hub — validate · delegate · broadcast only
├── Infrastructure/
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

- **Concurrency model** — how `GameRoomManager` uses a single `_lock` to make compound game operations atomic, and why `ConcurrentDictionary` alone is insufficient
- **SignalR hub design** — thin hub pattern (validate · delegate · broadcast), group management, and the disconnect/reconnect lifecycle
- **React + Razor architecture** — how the shell bootstraps React, the component tree, and why there is no build pipeline
- **Event subscription pattern** — how components subscribe in `useEffect` and always return unsubscribe functions to prevent listener leaks
- **Cleanup routine** — the `GameCleanupService` background timer and how `PurgeExpiredGames()` works under lock

---

## How a Turn Works

```
Player clicks Roll
  → gameHub.call('RollDice', gameId)
      → Hub validates it is the caller's turn
          → GameEngine.RollDice mutates GameState
              → Hub broadcasts GameStateUpdated + DiceRolled to all players
                  → React re-renders board, tokens, and cash
                  → Dice animation plays and settles on real values
```

The server is always the source of truth. The client never modifies game state locally — it waits for `GameStateUpdated` before re-rendering.

---

## Data Layer

- Active game state is held in both memory (`Dictionary<string, GameState>`) for low-latency reads and writes during play. 
- State is additionally persisted to JSON via file I/O, allowing the server to recover in-progress games across restarts without a database dependency. The persistence layer is isolated from the engine — adding a full database backend requires only a new `IGameStateStore` implementation.
- **NoSQL database Support**

---

## Non-Features (Intentional)

- **No auctions by design** — property stays unowned if declined; auctions slow games down
- **Roles or permissions is given via Admin Hub** — everyone can create and host but Admin has its own Panel Client and diagnostics view and is rate-limited
---

## License

MIT