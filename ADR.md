# Architecture Decision Record (ADR) - Monopoly Server

## Project Overview

Multiplayer Monopoly game server built with ASP.NET Core 10 and SignalR. Fully featured game engine with card system, trading, and self-managed lobbies. No database (in-memory), no auctions, no admin power creep.

---

## Core Game Systems (Completed)

### 1.1 Game Models - Data Only

Data structures with no business logic. Models are "dumb" containers for state.

**GameState** — Current game instance
- `GameId`, `HostId`, `Status` (Waiting | InProgress | Finished | Paused)
- `Board`, `Players`, `CurrentPlayerIndex`, `Turn`
- `CreatedAt`, `StartedAt`, `EndedAt`, `GameLog`
- `LastDiceRoll`, `DoubleRolled`, `PendingTrades`
- Simple getters only: `GetCurrentPlayer()`, `GetPlayerById()`, `LogAction()`

**Player** — Individual player state
- `Id`, `Name`, `Cash`, `Position`, `IsInJail`, `JailTurnsRemaining`
- `KeptCards` (Get Out of Jail Free cards)
- `IsBankrupt`, `JoinedAt`
- `IsCurrentPlayer`, `HasRolledDice`, `LastDiceRoll`
- Simple methods only: `AddCash()`, `DeductCash()`, `MoveTo()`, `SendToJail()`, etc.

**Board** — 40-space board
- `Spaces[40]` — array of all properties
- Methods: `GetProperty()`, `GetPropertiesByOwner()`, `GetPropertiesByColorGroup()`
- Initializes all streets, railroads, utilities, special spaces with correct prices/rent

**Property** — Individual board space
- `Id`, `Name`, `Type` (Street | Railroad | Utility | Tax | Chance | CommunityChest | FreeParking | GoToJail | Jail | Go)
- `Position`, `OwnerId`, `IsMortgaged`, `HouseCount`, `HasHotel`
- Financial: `PurchasePrice`, `MortgageValue`, `HouseCost`, `HotelCost`, `RentValues[]`
- `ColorGroup` (for monopoly checks)

**TradeOffer** — Pending trade between players
- `Id`, `FromPlayerId`, `ToPlayerId`, `Status` (Pending | Accepted | Rejected | Cancelled)
- `OfferedPropertyIds`, `OfferedCash`, `OfferedCardIds`
- `RequestedPropertyIds`, `RequestedCash`, `RequestedCardIds`
- `CreatedAt`, `RespondedAt`

### 1.2 Game Engine - All Business Logic

Single `GameEngine` class per game instance. Reads and mutates GameState. Validates all game rules.

**Turn & Movement:**
- `RollDice()` — returns (dice1, dice2, total, isDouble)
- `MovePlayer(int diceTotal)` — handles movement, passing Go ($200), landing logic
- `HandleLandingOnSpace(Player, position)` — executes space effects (buy, rent, jail, tax, cards)

**Property Transactions:**
- `BuyProperty(Property)` — validate ownership, cash, transfer
- `BuildHouse(Property)` — validate monopoly ownership, cash, build limits
- `BuildHotel(Property)` — validate 4 houses owned, cash, convert to hotel
- `MortgageProperty(Property)` — validate no houses/hotels, transfer cash
- `UnmortgageProperty(Property)` — validate cash, unmortgage

**Financial:**
- `CalculateRent(Property, diceRoll)` — determines rent based on property state
   - Streets: base, + per house/hotel, doubled if monopoly
   - Railroads: $25 × number owned
   - Utilities: dice roll × 4 (one owner) or × 10 (both owners)
- `CollectRent(payer, owner, amount)` — transfer money, trigger bankruptcy if needed
- `HandleTax(Player, Property)` — Income Tax ($200) or Luxury Tax ($100)

**Card System:**
- `DrawAndExecuteCard(CardDeck)` — draw from Chance or Community Chest, execute effect
- All 32 cards (16 Chance + 16 Community Chest) with full effects
   - Movement (Go, Jail, specific locations, railroads, utilities)
   - Financial (collect/pay bank, pay/collect each player)
   - House repairs (cost per house/hotel)
   - Get Out of Jail Free (kept by player, returned to deck when used)

**Jail System:**
- `SendPlayerToJail(Player)` — move to position 10, set jail flags
- `ReleaseFromJail(Player, payToBail)` — auto-release after 3 turns, doubles roll, or $50 payment
- `UseGetOutOfJailFreeCard(Player)` — use kept card, return to deck bottom

**Trading System:**
- `ProposeTrade(fromId, toId, offer)` — validate assets, add to pending trades
- `AcceptTrade(tradeId, playerId)` — execute atomic transfer (properties, cash, cards)
- `RejectTrade(tradeId, playerId)` — mark rejected
- `CancelTrade(tradeId, playerId)` — proposer cancels
- Private: `ExecuteTrade()` (atomic property/cash/card transfer), `ValidateTradeAssets()`

**Game Flow:**
- `StartGame()` — set status to InProgress, start with player 0
- `NextTurn()` — advance to next non-bankrupt player, increment turn
- `BankruptPlayer(Player)` — mark bankrupt, reclaim all properties, check win condition
- `EndGame(Player winner)` — set status to Finished, record end time

**Design Principle:** Engine is stateless (receives GameState, mutates it, returns nothing). Every public method validates preconditions and logs actions.

### 1.3 Card System - Full Classic Decks

**CardDeckManager** — Handles Chance and Community Chest decks

**Chance (16 cards):**
- Advance to Go
- Advance to Illinois Avenue (pos 24)
- Advance to St. Charles Place (pos 11)
- Advance to nearest Utility
- Advance to nearest Railroad (×2)
- Bank pays dividend ($50)
- Go back 3 spaces
- Go to Jail
- Get Out of Jail Free
- General repairs ($25/house, $100/hotel)
- Pay poor tax ($15)
- Trip to Reading Railroad (pos 5)
- Walk on Boardwalk (pos 39)
- Chairman of Board (pay each player $50)
- Building loan matures ($150)

**Community Chest (16 cards):**
- Advance to Go
- Bank error ($200)
- Doctor's fee ($50)
- Stock sale ($45)
- Get Out of Jail Free
- Go to Jail
- Holiday fund ($100)
- Income tax refund ($20)
- Birthday gift (collect $10 from each player)
- Life insurance ($100)
- Hospital fees ($50)
- School fees ($50)
- Consultancy fee ($25)
- Street repairs ($40/house, $115/hotel)
- Taxes due ($100)
- Crossword prize ($100)

**Mechanics:**
- Shuffle at game start
- Draw from front of queue
- Return to back of queue (except Get Out of Jail Free which is kept)
- Reshuffle if deck empties mid-game

### 1.4 Trading System - Service Layer

**TradeService** — Decoupled from SignalR, testable, reusable

Methods delegate to GameEngine but provide logging and validation:
- `ProposeTrade(gameId, fromId, toId, offer)` — returns TradeOffer or null
- `AcceptTrade(gameId, tradeId, playerId)` — returns bool
- `RejectTrade(gameId, tradeId, playerId)` — returns bool
- `CancelTrade(gameId, tradeId, playerId)` — returns bool
- `GetTrade(gameId, tradeId)` — returns TradeOffer or null
- `GetPendingTrades(gameId)` — returns list
- `GetPendingTradesForPlayer(gameId, playerId)` — returns trades awaiting response

**Design:** Service layer wraps GameEngine calls, adds logging, provides API surface for hub and tests.

### 1.5 Game Room Manager - Lifecycle

**GameRoomManager** — Thread-safe singleton, locks on state mutation

Methods:
- `CreateGame()` — generate unique 8-char ID, create GameState, return ID
- `GetGame(gameId)` — fetch GameState
- `GetGameEngine(gameId)` — fetch GameEngine for active game
- `SetGameEngine(gameId, engine)` — store engine when game starts
- `GetGameByPlayerId(playerId)` — find game containing player (for disconnect cleanup)
- `DeleteGame(gameId)` — remove game and engine
- `GetStats()` — return diagnostics (total games, in-progress, waiting, player counts)

**Design Principle:** Single source of truth. All access to games goes through this manager. Thread-safe via lock on internal dictionary.

---

## Lobby & Room System

### Problem Statement

Current implementation creates games but lacks:
- Public lobby to discover games
- Room management (waiting → start)
- Clean game lifecycle
- Player ready states
- Discovery & join flows

### Design Decision: Extend GameRoomManager

**Option A:** Separate LobbyService
- Pro: Clean separation
- Con: Duplicate state between lobby and games

**Option B:** Extend GameRoomManager (Chosen)
- Pro: Single source of truth
- Con: Manager does more

**Decision: Extend GameRoomManager with lobby queries**

### Game Lifecycle

**Waiting State:**
1. Host creates game → room enters `Waiting` status
2. Players join via `JoinGame(gameId, playerName)`
3. Broadcast `PlayerJoined` to all in room
4. Players see each other in lobby
5. Only host can start

**In Progress:**
1. Host calls `StartGame(gameId)`
2. Validate 2+ players
3. Create GameEngine
4. Set status to `InProgress`
5. No more joins allowed
6. Broadcast `GameStarted`

**Finished:**
1. GameEngine detects winner (1 player left)
2. Set status to `Finished`
3. Keep room for 30 seconds (allow stats viewing)
4. Auto-delete room after 30 seconds

**Leaving:**
- Player calls `LeaveGame(gameId)`
- Remove from players list
- If host left: assign new host to player[0]
- If last player: delete room immediately

### New GameRoomManager Methods

**Lobby queries:**
- `GetAvailableGames()` → List<GameRoomInfo> (only Waiting games)
- `GetGameLobby(gameId)` → GameRoomInfo (full room info with players)

**GameRoomInfo DTO:**
- `GameId`, `HostId`, `PlayerCount`, `MaxPlayers` (4)
- `Players[]` (list of PlayerInfo)
- `CreatedAt`

**PlayerInfo DTO:**
- `Id`, `Name`, `IsHost`

### SignalR Hub Methods - Extended

**Room Management:**
- `CreateGame(playerName)` → GameCreated event with gameId
- `JoinGame(gameId, playerName)` → PlayerJoined broadcast
- `LeaveGame(gameId)` → PlayerLeft broadcast
- `GetAvailableGames()` → returns List<GameRoomInfo>
- `GetGameLobby(gameId)` → returns GameRoomInfo

**Game Control:**
- `StartGame(gameId)` [host only] → GameStarted broadcast

**Game Play:** (existing methods)
- `RollDice(gameId)`
- `BuyProperty(gameId)`
- `ProposeTrade(gameId, toPlayerId, tradeDto)`
- etc.

### Hub Events - Broadcasts

**Lobby Events:**
- `GameCreated(gameId, playerId)` [to creator]
- `PlayerJoined(playerId, playerName, playerCount)` [to room]
- `PlayerLeft(playerCount)` [to room]
- `HostChanged(newHostName)` [to room, if host left]

**Game Events:**
- `GameStarted(currentPlayer, turn)` [to room]
- [existing game events]

---

## Data Transfer Objects (DTOs)

All SignalR serialization uses strongly-typed DTOs (no anonymous types, no `dynamic`).

**GameStateDto:**
- `GameId`, `Status`, `Turn`, `CurrentPlayerIndex`
- `CurrentPlayer` (PlayerDto or null)
- `Players[]` (List<PlayerDto>)
- `Board[]` (List<PropertyDto>)
- `GameLog` (List<string>, last 20 entries)

**PlayerDto:**
- `Id`, `Name`, `Cash`, `Position`
- `IsInJail`, `IsBankrupt`, `KeptCardCount`

**PropertyDto:**
- `Id`, `Name`, `Type`, `Position`
- `OwnerId`, `OwnerName`, `HouseCount`, `HasHotel`, `IsMortgaged`
- `ColorGroup`

**TradeOfferDto:**
- `Id`, `FromPlayerId`, `FromPlayerName`, `ToPlayerId`, `ToPlayerName`
- `OfferedPropertyIds[]`, `OfferedCash`, `OfferedCardIds[]`
- `RequestedPropertyIds[]`, `RequestedCash`, `RequestedCardIds[]`
- `Status`, `CreatedAt`

**GameRoomInfo:**
- `GameId`, `HostId`, `PlayerCount`, `MaxPlayers`
- `Players[]` (List<PlayerInfo>)
- `CreatedAt`

**PlayerInfo:**
- `Id`, `Name`, `IsHost`

---

## Dependency Injection & Architecture

### Service Registration (Program.cs)

All singletons (shared across all game instances):
- `GameRoomManager` — room lifecycle
- `TradeService` — trade logic
- `ILogger<T>` — logging

Per-game instances (created fresh):
- `GameEngine` — created when game starts, stored in GameRoomManager

### Hub Dependency Injection

GameHub receives:
- `GameRoomManager` — access to all games
- `TradeService` — delegate trade operations
- `ILogger<GameHub>` — logging

Hub methods validate, delegate to services, broadcast updates.

### Separation of Concerns

| Layer | Purpose | Examples |
|-------|---------|----------|
| **Models** | Data only | GameState, Player, Property |
| **Engine** | Game rules | GameEngine, CardDeckManager |
| **Services** | Orchestration | GameRoomManager, TradeService |
| **Hub** | Transport | GameHub (SignalR endpoint) |
| **DTOs** | Serialization | GameStateDto, TradeOfferDto |

**Rule:** Hub never calls GameEngine directly. Always goes through services or GameState reads.

---

## Edge Cases & Validation

| Scenario | Behavior |
|----------|----------|
| Player joins full game | Error: "Game is full" |
| Try to start with <2 players | Error: "Need at least 2 players" |
| Try to start game as non-host | Error: "Only host can start" |
| Host leaves during lobby | New host = player[0] |
| Last player leaves | Room deleted immediately |
| Player joins finished game | Error: "Game has ended" |
| Player disconnects mid-turn | Treated as LeaveGame, player bankrupted |
| Reconnect to game | GetGameState() restores full state |
| Try to buy property twice | Error: "Already owned" |
| Can't afford property | Error message, no purchase |
| Property with houses can't be mortgaged | Engine validates in `MortgageProperty()` |
| Jail: roll doubles → release immediately | Engine checks in `ReleaseFromJail()` |
| Trade for property don't own | TradeService validates assets |
| Card: "Go back 3" lands on Chance | Chain effect: draw next card |
| Player bankrupted mid-trade | Trade cancels, properties reclaimed |

---

## Non-Features (Intentional Exclusions)

### No Auctions
**Why:** Auctions force bitter gameplay. Players can decline to buy and leave property unowned. Decision to skip auctions was deliberate after user feedback.

### No Database/Persistence
**Why:** In-memory only. Games exist for session lifetime. Server restart clears all games. Acceptable for MVP. Can add later if needed.

### No Admin Powers
**Why:** Host = normal player. No special privileges. Prevents admin abuse in public lobbies.

### No Roles or Permissions
**Why:** Keep it simple. 4 players, equal standing, winner determined by gameplay.

### No Auction System, No Free Parking Pot
**Why:** Unnecessary complexity. Classic rules only.

---

## Technology Decisions

| Decision | Technology | Why |
|----------|-----------|-----|
| Language | C# (ASP.NET Core 10) | Type-safe, fast, SignalR native |
| Transport | SignalR | Real-time, reliable, handles disconnects |
| In-memory Storage | Dictionary<string, GameState> | Simple, thread-safe with locks |
| Game Loop | State-driven | Engine mutates state, hub broadcasts |
| Serialization | JSON DTOs | Type-safe, no dynamic serialization issues |
| Architecture | Service-based | Decoupled, testable, extensible |

---

### Flow

**Registration/Login:**

**Hub Connection:**
- Hub methods can access `Context.User.Identity.Name` for player ID
- All game operations use authenticated player ID

### Identity Model

### Authorization Service

### Hub Integration

GameHub methods can now access authenticated player:
```
var playerId = Context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var username = Context.User.Identity.Name;
```

### Database Storage (Optional)

For MVP: In-memory user store (Dictionary<string, User>)
Future: Upgrade to SQL Server, PostgreSQL, or MongoDB

---

## Lobby Management

### Lobby Service

New service layer for room/lobby operations, separate from GameRoomManager.

**LobbyService** — Manages public lobby state

Responsibilities:
- Track available games
- Validate join/create operations
- Send lobby updates to connected clients
- Auto-cleanup finished games

### Lobby State

**LobbyCache:**
- Dictionary of GameId → GameRoomInfo
- Auto-updated when games change status
- Broadcast updates to all lobby listeners

### Hub Methods - Lobby

**Discovery:**
- `GetAvailableGames()` → List<GameRoomInfo> (cached, instant)
- `GetGameLobby(gameId)` → GameRoomInfo or error

**Management:**
- `CreateGame(gameName)` → GameCreated event with gameId
- `JoinGame(gameId)` → PlayerJoined broadcast to room
- `LeaveGame(gameId)` → PlayerLeft broadcast to room
- `GetMyGames()` → games user created or joined (future)

### Broadcasting Strategy

**Lobby Broadcasts (to all connected clients):**
- `AvailableGamesUpdated(games)` — when game created/started/finished
- `GameRoomUpdated(gameId, roomInfo)` — when player joins/leaves room

**Room Broadcasts (to players in specific game):**
- `PlayerJoined(playerInfo)` — someone joined your room
- `PlayerLeft(playerName)` — someone left your room
- `GameStarting()` — host is starting
- `GameStarted()` — game has begun

**Game Broadcasts (to players in active game):**
- `DiceRolled()`
- `PropertyBought()`
- etc.

### Game Lifecycle with Auth

**Creation:**
1. Authenticated player calls `CreateGame("Game Name")`
2. Hub validates user is logged in
3. GameRoomManager creates game with authenticated playerId as host
4. LobbyService caches game as available
5. Broadcast `AvailableGamesUpdated` to all lobby listeners

**Joining:**
1. Authenticated player calls `JoinGame(gameId)`
2. Hub validates user is logged in, game is Waiting
3. GameRoomManager adds player to room
4. LobbyService updates cache
5. Broadcast `GameRoomUpdated` to room, `AvailableGamesUpdated` to lobby

**Starting:**
1. Host calls `StartGame(gameId)`
2. Hub validates user is host, 2+ players
3. GameEngine created, status → InProgress
4. LobbyService removes from available games
5. Broadcast `AvailableGamesUpdated` (game disappears from lobby)
6. Broadcast `GameStarted` to room (game begins)

**Finishing:**
1. GameEngine detects winner
2. Status → Finished
3. Keep room for 30 seconds
4. Auto-delete after 30 seconds
5. LobbyService removes from available games

### Edge Cases - Auth & Lobby

| Scenario | Behavior |
|----------|----------|
| Try to create game without auth | Error: "Must be logged in" |
| Host logout during game | Game continues, host flag stays with player ID |
| New host logout during lobby | New host auto-assigned to next player |
| Username collision | Error: "Username already taken" |
| Invalid/expired token | Disconnect and require re-login |
| Join game with same name as existing player | Error: "Name already in game" |
| Rejoin after disconnect | Get game state, rejoin same room |
| Create game with empty name | Error: "Game name required" |

### Non-Features - Auth

- No roles (user, admin, moderator)
- No permissions (everyone can start games they created)
- No bans/moderation (out of scope)
- No password reset (MVP only)
- No email verification (future)

---

## Client Architecture (Next Steps)

### Tech Stack
- Flutter (mobile + web)
- SignalR client for Dart
- Native platform features

### Pages

**Lobby Screen:**
- List available games (auto-refresh)
- Create game button
- Join button per game
- Game info (player count, created time)

**Room/Waiting Screen:**
- Players in room with host indicator
- "Start Game" button (host only)
- Leave button
- Ready indicator (optional)

**Game Screen:**
- 40-space board with player tokens
- Player panels (cash, properties, jail status)
- Action buttons (roll, buy, build, mortgage, trade, end turn)
- Game log (last 20 events)
- Trade notifications & dialogs

### Implementation Phases

1. **Phase 1:** Extend GameRoomManager with lobby methods
2. **Phase 2:** Build Lobby Screen (list games, create, join)
3. **Phase 3:** Build Room/Waiting Screen (players, start)
4. **Phase 4:** Build Game Board Screen (layout, tokens)
5. **Phase 5:** Wire up game mechanics UI (buttons, dialogs)

---

## Comparison to Alternatives

| Feature | Current Design | Auction System | Admin Roles |
|---------|---|---|---|
| Simplicity | Simple | Complex | Over-engineered |
| Speed of Play | Fast | Slow | Fast |
| Fair Gameplay | Skill-based | Yes | Admin bias |
| Self-managed | Yes | Yes | Yes |

---

## Summary

**What We've Built:**
- Complete game engine with all Monopoly rules
- Full card system (32 cards, proper effects)
- Trading system (propose, accept, reject, cancel)
- Thread-safe room manager
- Lobby service (game discovery, room management)
- Clean DI architecture with services
- Type-safe DTOs for all serialization

**What's Next:**
- Implement simple Local Login by name and unique ID (no password)
- Implement LobbyService (game discovery, caching)
- Build Flutter client (login, lobby, room, game board)

**Design Principles:**
- Models are dumb (data only)
- Engine is stateless (receives GameState, returns nothing)
- Services are decoupled (testable, no SignalR)
- Hub is thin (delegates, broadcasts)
- All mutations validated
- Thread-safe (locks in GameRoomManager)
- Authenticated players required for game operations
- No unnecessary features (no auctions, no DB, no admin abuse, no permissions)

**Result:** A fully-featured Monopoly server with auth and lobby management, ready for Flutter client. Clean architecture, easy to test, easy to extend.