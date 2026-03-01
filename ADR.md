# Architecture Decision Record — Monopoly Online

## Overview

Multiplayer Monopoly built on ASP.NET Core 10 with a SignalR hub and a React client served through a static HTML shell.
Complete game engine covering all classic rules — card system, trading, jail, rent, and buildings. Active game state is
held in memory for performance and additionally persisted to JSON for recovery across restarts. No database, no auctions,
no admin privilege creep.

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

## Core Game Systems

### Models — Data Only

Dumb containers. No business logic lives in models.

**GameState** — live game instance  
`GameId` · `HostId` · `Status` (Waiting | InProgress | Finished | Paused) · `Board` · `Players` · `CurrentPlayerIndex` ·
`Turn` · `CreatedAt` · `StartedAt` · `EndedAt` · `GameLog` · `LastDiceRoll` · `DoubleRolled` · `PendingTrades`  
Getters only: `GetCurrentPlayer()` · `GetPlayerById()` · `LogAction()`

**Player** — per-player state  
`Id` · `Name` · `Cash` · `Position` · `IsInJail` · `JailTurnsRemaining` · `KeptCards` · `IsBankrupt` ·
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

Active game state is held in memory (`Dictionary<string, GameState>`) for low-latency reads and writes during play. State
is additionally persisted to JSON via file I/O, allowing the server to recover in-progress games across restarts without
a database dependency. Writes are scoped to meaningful transitions (game start, turn end, state change) rather than every
mutation, keeping I/O overhead negligible.

The persistence layer is isolated from the engine. `GameRoomManager` owns the write path; the engine has no knowledge of
storage. Replacing file-backed JSON with a full database backend requires only a new `IGameStateStore` implementation
without touching engine or hub code.

---

## Concurrency

### Problem

`GameRoomManager` is a singleton shared across every concurrent SignalR connection. Two players acting simultaneously —
both attempting to buy the same property, or a player acting while a disconnect-cleanup fires — can corrupt `GameState`
without explicit synchronisation.

### Decision — Single Lock Per Manager

All reads that feed a mutation, and all mutations themselves, are performed inside a single `object _lock`. The lock is
held only for the duration of the state operation, never across an `await`.

```csharp
private readonly object _lock = new();
private readonly Dictionary<string, GameState>  _games   = new();
private readonly Dictionary<string, GameEngine> _engines = new();

public string CreateGame()
{
    lock (_lock)
    {
        var id = GenerateId();
        _games[id] = new GameState(id);
        return id;
    }
}

public (GameState state, GameEngine engine) GetGameAndEngine(string gameId)
{
    lock (_lock)
        return (_games[gameId], _engines[gameId]);
}
```

**Why not `ConcurrentDictionary`:** Individual dictionary operations would be atomic, but game actions are compound —
read player state, validate, mutate board, write log. That entire sequence must be atomic as a unit, which
`ConcurrentDictionary` cannot guarantee.

**Why not per-game locks:** Trades span two players who may arrive from different hub invocations concurrently. A single
manager-level lock eliminates any lock-ordering deadlock risk and is straightforward to reason about. With a four-player
cap, contention is negligible.

### Lock and Broadcast Pattern

Broadcasts must never happen inside the lock. The consistent pattern throughout the hub is:

```csharp
// 1. Mutate under lock, capture what we need
GameState state;
lock (_lock) { state = rooms.Mutate(gameId, ...); }

// 2. Broadcast after lock is released
await Clients.Group(gameId).SendAsync("GameStateUpdated", mapper.ToDto(state));
```

Holding a lock across an `await` would starve the thread pool and risk deadlock with other hub invocations waiting on
the same lock.

### Cleanup Routine

A `BackgroundService` runs on a 60-second interval and purges stale rooms. It handles two cases: `Finished` games older
than 30 seconds, and `Waiting` rooms with no players remaining.

```csharp
public class GameCleanupService(GameRoomManager rooms, ILogger<GameCleanupService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), ct);

            var purged = rooms.PurgeExpiredGames();
            if (purged > 0)
                log.LogInformation("Cleanup purged {Count} expired games", purged);
        }
    }
}
```

`PurgeExpiredGames()` runs entirely inside `_lock`, iterating the dictionary once and removing qualifying entries in a
single pass. It returns the purge count for log observability.

---

## SignalR Hub Architecture

### Design — Thin Hub, Three-Step Pattern

`GameHub` is a transport boundary only. It contains no game logic. Every hub method follows the same three steps:

```
1. Validate  — is the caller permitted to do this right now?
2. Delegate  — pass to GameRoomManager or TradeService
3. Broadcast — push updated GameStateDto to the SignalR group
```

```csharp
public async Task RollDice(string gameId)
{
    var (state, engine) = _rooms.GetGameAndEngine(gameId);

    if (state.GetCurrentPlayer()?.ConnectionId != Context.ConnectionId)
        throw new HubException("Not your turn");

    engine.RollDice(state);

    await Clients.Group(gameId).SendAsync("GameStateUpdated", _mapper.ToDto(state));
    await Clients.Group(gameId).SendAsync("DiceRolled", state.LastDiceRoll.D1, state.LastDiceRoll.D2);
}
```

**Invariant:** The hub never calls `GameEngine` directly. All access goes through `GameRoomManager`, which owns `_lock`.
The engine is always invoked from within a manager method that already holds the lock.

### Hub Groups

Each game is a SignalR group keyed by `gameId`. Players join the group on `JoinGame` and leave on `LeaveGame` or
disconnect. `OnDisconnectedAsync` finds the player's game by `ConnectionId` and resigns them if the game is in progress.

```csharp
// Join
await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

// Leave / disconnect
await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
```

```csharp
public override async Task OnDisconnectedAsync(Exception? ex)
{
    var game = _rooms.GetGameByConnectionId(Context.ConnectionId);
    if (game is not null)
        await HandlePlayerDisconnect(game.GameId);

    await base.OnDisconnectedAsync(ex);
}
```

### Hub Methods

**Room management:**

| Method                            | Broadcasts                                  |
|-----------------------------------|---------------------------------------------|
| `CreateGame(playerName)`          | `GameCreated` → creator                     |
| `JoinGame(gameId, playerName)`    | `PlayerJoined` → room                       |
| `LeaveGame(gameId)`               | `PlayerLeft` / `HostChanged` → room         |
| `StartGame(gameId)` *(host only)* | `GameStarted` → room                        |
| `GetAvailableGames()`             | Returns `List<GameRoomInfo>` to caller only |
| `GetGameLobby(gameId)`            | Returns `GameRoomInfo` to caller only       |

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

**Admin:**

| Method                         | Broadcasts                                |
|--------------------------------|-------------------------------------------|
| `PauseGame(gameId)`            | `GamePaused` → room                       |
| `ResumeGame(gameId)`           | `GameResumed` → room                      |
| `KickPlayer(gameId, playerId)` | `PlayerKicked` → room · `Kicked` → target |
| `ForceEndGame(gameId)`         | `GameForceEnded` → room                   |

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

```
MonopolyClient/wwwroot/
    globals.js               ← React hook aliases (useState, useEffect, …) on window
    app.js                   ← Root component and page router
    utils/
        constants.js         ← SPACES, COLORS, BCOLORS — shared game data
        hub_service.js       ← SignalR connection factory and helpers
    animation/
        animation.js         ← usePlayerHop, useDiceRoll, DiceTray, ChestCardPopup
    components/
        board.js             ← Board component with scaled cells and token overlay
        header.js            ← Top bar — room code, player list, connection status
        toasts.js            ← Transient notification system
        turn_timer.js        ← Countdown display, auto-advance warning
    pages/
        home_page.js         ← Landing page — create or join a room
        lobby_page.js        ← Pre-game lobby — player list, start button
        game_page.js         ← GamePage — hub wiring, all game state, action panel
        admin_page.js        ← Server health and diagnostics view
```

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
and `call`, and reconnects automatically.

```js
const gameHub = (() => {
    const conn = new signalR.HubConnectionBuilder()
        .withUrl(window.HUB_URL)
        .withAutomaticReconnect()
        .build();

    return {
        start: () => conn.start(),

        /** Subscribe to a server event. Returns an unsubscribe function. */
        on(event, handler) {
            conn.on(event, handler);
            return () => conn.off(event, handler);
        },

        /** Invoke a hub method. Returns a Promise. */
        call(method, ...args) {
            return conn.invoke(method, ...args);
        },
    };
})();
```

`withAutomaticReconnect()` uses exponential back-off for transient drops. The hub fires `Reconnected` on successful
reconnect, which the client handles by calling `JoinGame` again to rejoin the SignalR group and receive a fresh
`GameStateUpdated`.

### Event Subscription Pattern

Components subscribe inside `useEffect` and return each unsubscribe function as cleanup. This guarantees handlers are
torn down when the component unmounts and prevents duplicate listener accumulation across page transitions or StrictMode
double-invocations.

```js
useEffect(() => {
    const unsubs = [
        gameHub.on('GameStateUpdated', state          => setGameState(state)),
        gameHub.on('DiceRolled',       (d1, d2)       => settleDice([d1, d2])),
        gameHub.on('CardDrawn',        card            => setDrawnCard(card)),
        gameHub.on('TradeProposed',    offer           => setIncomingTrade(offer)),
        gameHub.on('GamePaused',       ()              => setPaused(true)),
        gameHub.on('GameResumed',      ()              => setPaused(false)),
        gameHub.on('TurnWarning',      ({ message })   => toast(`⏰ ${message}`, 'warning')),
        gameHub.on('PlayerKicked',     ({ playerName }) => toast(`${playerName} was removed`)),
        gameHub.on('GameForceEnded',   ()              => { toast('Game ended', 'error'); onLeave(); }),
        gameHub.on('Kicked',           msg             => { toast(msg, 'error'); onLeave(); }),
        gameHub.on('Reconnected',      ()              => gameHub.call('JoinGame', gameId, playerName)),
    ];

    return () => unsubs.forEach(fn => fn());
}, [gameId, playerName]);
```

Each `gameHub.on()` call returns its own teardown. Collecting them and calling each in cleanup ensures no handler leaks
regardless of how many events a component registers.

### State Flow — Server is Source of Truth

The client never optimistically mutates game state. It updates only after receiving `GameStateUpdated`. Local state is
purely presentational: dice animation phase, open modals, selected trade target.

```
User clicks Roll
  → gameHub.call('RollDice', gameId)
      → Hub validates turn, delegates to engine, engine mutates GameState
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

| Method                          | Description                                              |
|---------------------------------|----------------------------------------------------------|
| `CreateGame()`                  | Generate 8-char ID, create `GameState`, return ID        |
| `GetGame(gameId)`               | Fetch `GameState`                                        |
| `GetGameEngine(gameId)`         | Fetch engine for active game                             |
| `SetGameEngine(gameId, engine)` | Store engine when game starts                            |
| `GetGameAndEngine(gameId)`      | Fetch both atomically under single lock                  |
| `GetGameByConnectionId(connId)` | Find game for disconnect handling                        |
| `DeleteGame(gameId)`            | Remove game and engine                                   |
| `PurgeExpiredGames()`           | Remove stale rooms; called by cleanup routine            |
| `GetStats()`                    | Diagnostics — total, in-progress, waiting, player counts |

### TradeService

Decoupled from SignalR entirely — no hub references, independently testable. Wraps engine trade calls with
pre-validation and structured logging.

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

| Method                              | Description                                         |
|-------------------------------------|-----------------------------------------------------|
| `CreateRoom(playerName, connId)`    | Initialise room, assign host                        |
| `JoinRoom(gameId, playerName, connId)` | Validate and add player to waiting room          |
| `LeaveRoom(gameId, playerId)`       | Remove player; promote new host if needed           |
| `GetAvailableRooms()`               | Returns joinable rooms                              |

### TurnTimerService

Manages per-game countdown timers. Fires `TurnWarning` to the current player before auto-advancing the turn on expiry.

### Registration (Program.cs)

```csharp
builder.Services.AddSingleton<GameRoomManager>();
builder.Services.AddSingleton<TradeService>();
builder.Services.AddSingleton<LobbyService>();
builder.Services.AddSingleton<TurnTimerService>();
builder.Services.AddHostedService<GameCleanupService>();

builder.Services.AddSignalR();

app.MapHub<GameHub>("/gamehub");
app.MapHub<AdminHub>("/adminhub");
```

Singletons are safe because all mutation is gated by `_lock` inside `GameRoomManager`. `GameCleanupService` receives
`GameRoomManager` via constructor injection and runs the purge on a background timer without any additional
synchronisation needed.

---

## Data Transfer Objects

All SignalR serialisation uses strongly-typed DTOs. No anonymous types, no `dynamic`.

**`GameStateDto`** — `GameId` · `Status` · `Turn` · `CurrentPlayer` · `Players[]` · `Board[]` · `EventLog` (last 20
entries) · `CurrentTurnStartedAt`

**`PlayerDto`** — `Id` · `Name` · `Cash` · `Position` · `IsInJail` · `IsBankrupt` · `KeptCardCount` · `HasRolledDice` ·
`IsConnected`

**`PropertyDto`** — `Id` · `Name` · `Type` · `Position` · `OwnerId` · `HouseCount` · `HasHotel` · `IsMortgaged` ·
`ColorGroup` · `PurchasePrice`

**`TradeOfferDto`** — `Id` · `FromPlayerId` · `FromPlayerName` · `ToPlayerId` · `OfferedPropertyIds[]` · `OfferedCash` ·
`RequestedPropertyIds[]` · `RequestedCash` · `Status` · `CreatedAt`

**`GameRoomInfo`** — `GameId` · `HostId` · `PlayerCount` · `MaxPlayers` · `Players[]` · `CreatedAt`

**`PlayerLobbyInfo`** — Lightweight player projection used in lobby broadcasts before game start

**`ServerHealthStatsDto`** — Diagnostics payload served by `AdminHub`

**`CardDto`** — `Type` · `Text` · `Amount?`

---

## Game Lifecycle

**Waiting** — Host creates room → players join via `JoinGame` → `PlayerJoined` broadcast to room → only host can start.

**In Progress** — `StartGame` validates 2+ players → engine created and stored → status → `InProgress` → `GameStarted`
broadcast → no further joins allowed.

**Finished** — Engine detects last standing player → `EndGame(winner)` → status → `Finished` → cleanup routine removes
room after 30 s.

**Leaving during lobby** — Player removed → if host left, `player[0]` promoted → `HostChanged` broadcast. Last player
leaves → room deleted immediately.

**Disconnect mid-game** — `OnDisconnectedAsync` finds game by `ConnectionId` → player bankrupted → properties reclaimed
by bank → pending trades cancelled → `GameStateUpdated` broadcast to remaining players.

**Reconnect** — `withAutomaticReconnect()` restores transport → client's `Reconnected` handler calls `JoinGame` → hub
re-adds connection to the group and sends current `GameStateDto` directly to that connection.

---

## Edge Cases

| Scenario                        | Behaviour                                                    |
|---------------------------------|--------------------------------------------------------------|
| Player joins full game          | Error: "Game is full"                                        |
| Start with fewer than 2 players | Error: "Need at least 2 players"                             |
| Non-host calls `StartGame`      | Error: "Only host can start"                                 |
| Host leaves lobby               | `player[0]` promoted, `HostChanged` broadcast                |
| Last player leaves              | Room deleted immediately                                     |
| Player disconnects mid-turn     | Resigned, bankrupted, state broadcast to room                |
| Reconnect to active game        | Full `GameStateDto` sent to reconnecting connection          |
| Buy already-owned property      | Engine rejects: "Already owned"                              |
| Insufficient cash               | Engine rejects with reason, no state change                  |
| Mortgage with houses present    | Engine rejects in `MortgageProperty()`                       |
| Jail — roll doubles             | Engine releases immediately in `ReleaseFromJail()`           |
| Trade for unowned property      | `TradeService.ValidateTradeAssets()` rejects                 |
| "Go back 3" lands on Chance     | Chain draw: next card executed immediately                   |
| Player bankrupted mid-trade     | Trade cancelled, properties returned to bank                 |
| Concurrent buy attempts         | `_lock` ensures first writer wins; second receives rejection |

---

## Intentional Exclusions

**No auctions by design** — Property stays unowned if declined. Auctions prolong games and were removed after feedback.

**NoSQL database support** — State is persisted to JSON for recovery; a relational store can be added later by implementing
`IGameStateStore` without touching the engine.

**Roles or permissions is given via Admin Hub** — everyone can create and host but Admin has its own Panel Client and diagnostics view and is rate-limited

---

## Architecture Invariants

| Layer            | Responsibility                                        |
|------------------|-------------------------------------------------------|
| **Models**       | Data containers — no logic                            |
| **Engine**       | All game rules — stateless, validated, logged         |
| **Services**     | Orchestration, locking, cleanup                       |
| **Hub**          | Transport only — validate · delegate · broadcast      |
| **DTOs**         | Typed serialisation boundary                          |
| **Persistence**  | JSON file I/O, isolated from engine and hub           |
| **React**        | Presentational — server is the single source of truth |

- Hub never calls `GameEngine` directly — always via `GameRoomManager`
- State mutations only happen inside `_lock`
- Broadcasts happen after the lock is released, never inside it
- React components always return their unsubscribe functions from `useEffect`
- Client never mutates game state locally — waits for `GameStateUpdated`