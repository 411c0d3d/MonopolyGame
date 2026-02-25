# System Architecture Decision Record (ADR)

## ASP.NET Core with SignalR for Real-Time Multiplayer Game Server

MonopolyServer/
├── src/
│   ├── MonopolyServer.Api/          # ASP.NET Core entry point
│   │   ├── Hubs/
│   │   │   └── GameHub.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── MonopolyServer.Game/         # Game engine, pure C#, no web dependencies
│   │   ├── Engine/
│   │   │   └── GameEngine.cs
│   │   ├── Models/
│   │   │   ├── GameState.cs
│   │   │   ├── Player.cs
│   │   │   ├── Board.cs
│   │   │   └── Property.cs
│   │   └── Services/
│   │       └── GameRoomManager.cs
│   └── MonopolyServer.Tests/
├── Dockerfile
├── docker-compose.yml               # local dev with Azure SignalR emulator or local SignalR
└── .github/workflows/deploy.yml     # build, push to ACR, deploy to Container Apps


## Monopoly Game Architecture

### Overview

The Monopoly game is built with a clean separation of concerns:

- **Models** (`MonopolyServer.Game/Models/`) — Pure data structures, no logic
- **Engine** (`MonopolyServer.Game/Engine/`) — Game logic and rules
- **Services** (`MonopolyServer.Game/Services/`) — Room management and persistence
- **API Hub** (`MonopolyServer.Api/Hubs/`) — SignalR communication layer

---

## Data Model

### GameState

**Purpose:** Represents the current state of an entire Monopoly game session.

**Properties:**
- `GameId` (string) — Unique identifier for this game session
- `HostId` (string?) — Player ID of the host/creator
- `Status` (GameStatus enum) — Waiting | InProgress | Finished | Paused
- `Board` (Board) — The game board with all 40 spaces
- `Players` (List<Player>) — All players in the game (2-4 players)
- `CurrentPlayerIndex` (int) — Index in Players list of whose turn it is
- `Turn` (int) — Current turn number (increments each round)
- `CreatedAt` (DateTime) — When the game was created
- `StartedAt` (DateTime?) — When the game started
- `EndedAt` (DateTime?) — When the game ended
- `GameLog` (List<string>) — Timestamped log of all actions
- `LastDiceRoll` (int) — Sum of the last dice roll (used for utility rent)
- `DoubleRolled` (bool) — Whether the last roll was doubles (triggers extra turn)

**Responsibilities:**
- Hold current game state
- Provide simple getters: `GetCurrentPlayer()`, `GetPlayerById()`
- Log actions to game history
- **No business logic—that's GameEngine's job**

---

### Player

**Purpose:** Represents a single player in the game.

**Properties:**
- `Id` (string) — Unique player ID (usually from SignalR connection)
- `Name` (string) — Display name
- `Cash` (int) — Current cash on hand (starts at $1500)
- `Position` (int) — Current position on board (0-39)
- `IsInJail` (bool) — Whether they're currently in jail
- `JailTurnsRemaining` (int) — How many turns they must stay in jail
- `HasGetOutOfJailFree` (bool) — Whether they have a "Get Out of Jail Free" card
- `IsBankrupt` (bool) — Whether they've been eliminated from the game
- `JoinedAt` (DateTime) — When they joined the game
- `IsCurrentPlayer` (bool) — Whether it's their turn right now
- `HasRolledDice` (bool) — Whether they've rolled in this turn (prevents rolling twice)
- `LastDiceRoll` (int) — Their last dice roll result

**Responsibilities:**
- Track player cash and position
- Track jail status
- **No logic beyond simple methods like `AddCash()`, `DeductCash()`, `MoveTo()`**

---

### Board

**Purpose:** Represents the 40-space Monopoly board and provides property queries.

**Properties:**
- `Spaces` (Property[]) — Array of 40 properties on the board

**Key Methods:**
- `GetProperty(int position)` — Fetch a space by board position (0-39)
- `GetPropertiesByOwner(string ownerId)` — Get all properties owned by a player
- `GetPropertiesByColorGroup(string colorGroup)` — Get all properties in a color group (for monopoly checks)

**Board Layout:**
```
  
Color Groups:
- Brown: positions 1, 3
- Light Blue: positions 6, 8, 9
- Pink: positions 11, 13, 14
- Orange: positions 16, 18, 19
- Red: positions 21, 23, 24
- Yellow: positions 26, 27, 29
- Green: positions 31, 32, 34
- Dark Blue: positions 37, 39
- Railroads: positions 5, 15, 25, 35
- Utilities: positions 12, 28
- Special: 0 (Go), 10 (Jail), 20 (Free Parking), 30 (Go to Jail), 2/17/33 (Community Chest), 4/38 (Tax), 7/22/36 (Chance)
```

**Responsibilities:**
- Initialize all 40 board spaces with correct prices and rent values
- Provide lookup methods for game logic
- **No game logic—just data access**

---

### Property

**Purpose:** Represents a single board space (street, railroad, utility, tax, special).

**Properties:**

**Core:**
- `Id` (int) — Unique property ID (0-39, matching board position)
- `Name` (string) — Property name ("Boardwalk", "Go", "Free Parking", etc.)
- `Type` (PropertyType enum) — Street | Railroad | Utility | Tax | Chance | CommunityChest | FreeParking | GoToJail | Jail | Go
- `Position` (int) — Board position (0-39)

**Ownership & Development:**
- `OwnerId` (string?) — Player ID of owner (null if unowned)
- `IsMortgaged` (bool) — Whether the property is mortgaged (no rent collected, can't build)
- `HouseCount` (int) — Number of houses (0-4, hotels are tracked separately)
- `HasHotel` (bool) — Whether it has a hotel (replaces 4 houses)

**Financial (Streets Only):**
- `PurchasePrice` (int) — Cost to buy
- `MortgageValue` (int) — Cash received when mortgaged (50% of purchase price)
- `HouseCost` (int) — Cost to build one house
- `HotelCost` (int) — Cost to build one hotel
- `RentValues` (int[]) — Array of rents: [base, 1 house, 2 houses, 3 houses, 4 houses, hotel]

**Color Group (Streets Only):**
- `ColorGroup` (string?) — Color name for monopoly grouping ("Brown", "Light Blue", etc.)

**Example - Boardwalk:**
```
Name: "Boardwalk"
Position: 39
Type: Street
PurchasePrice: $400
MortgageValue: $200
HouseCost: $200
HotelCost: $150
RentValues: [50, 200, 600, 1400, 1700, 2000]
ColorGroup: "Dark Blue"
```

**Example - Reading Railroad:**
```
Name: "Reading Railroad"
Position: 5
Type: Railroad
PurchasePrice: $200
MortgageValue: $100
(Rent is calculated: $25 × number of railroads owned)
```

**Example - Electric Company:**
```
Name: "Electric Company"
Position: 12
Type: Utility
PurchasePrice: $150
MortgageValue: $75
(Rent is calculated: dice roll × 4 or dice roll × 10 depending on utilities owned)
```

**Responsibilities:**
- Hold property financial and ownership data
- Provide `GetCurrentRent()` method for quick rent lookups
- **No complex logic—GameEngine calculates actual rent owed**

---

## Game Engine (Business Logic)

**Purpose:** `GameEngine` contains all Monopoly rules and turn logic. It reads/writes to GameState.

**Key Responsibilities:**

### Turn & Movement
- `RollDice()` — Roll 2d6, detect doubles
- `MovePlayer(int diceTotal)` — Move current player, handle passing Go
- `HandleLandingOnSpace(Player, int position)` — Execute landing logic

### Property Transactions
- `BuyProperty(Property)` — Current player buys an unowned property
- `BuildHouse(Property)` — Build house on a property (requires monopoly)
- `BuildHotel(Property)` — Build hotel on a property (requires 4 houses)
- `MortgageProperty(Property)` — Mortgage property for cash
- `UnmortgageProperty(Property)` — Pay to unmortgage

### Financial
- `CalculateRent(Property, diceRoll)` — Determine rent owed based on property state
- `CollectRent(payer, owner, amount, propertyName)` — Transfer money, handle bankruptcy
- `HandleTax(Player, Property)` — Pay income/luxury tax

### Jail
- `SendPlayerToJail(Player)` — Force player to jail
- `ReleaseFromJail(Player, payToBail)` — Release from jail (after 3 turns, doubles, or $50 payment)

### Game Flow
- `StartGame()` — Initialize game state and first player
- `NextTurn()` — Advance to next player (skip bankrupt players)
- `BankruptPlayer(Player)` — Eliminate player, reclaim properties
- `EndGame(Player winner)` — Declare winner

**Example Flow:**
```
1. GameEngine.RollDice() → returns (3, 4, 7, false)
2. GameState.LastDiceRoll = 7, DoubleRolled = false
3. GameEngine.MovePlayer(7) → moves current player 7 spaces
4. GameEngine.HandleLandingOnSpace(player, newPosition)
   - If property unowned → offer to buy
   - If property owned by other → GameEngine.CalculateRent() → GameEngine.CollectRent()
5. GameEngine.NextTurn() → advance to next player
```

---

## Service Layer

### GameRoomManager

**Purpose:** Manages game room lifecycle (creation, player joining, room cleanup).

**Responsibilities:**
- Create new games
- Add/remove players from games
- Retrieve active games
- Cleanup when games end

---

## API Layer (SignalR Hub)

### GameHub

**Purpose:** Handle real-time communication between clients and server.

**Client → Server Methods:**
- `JoinGame(gameId, playerName)` — Join an existing game
- `RollDice()` — Ask engine to roll
- `MovePlayer()` — Tell engine to move (after roll)
- `BuyProperty(propertyId)` — Ask to buy property
- `BuildHouse(propertyId)` — Ask to build
- `MortgageProperty(propertyId)` — Ask to mortgage
- `EndTurn()` — Manually end turn

**Server → Client Events:**
- `GameStateUpdated(gameState)` — Send updated board state
- `PlayerMovedEvent(playerId, newPosition)` — Announce player movement
- `RentPaidEvent(fromPlayer, toPlayer, amount)` — Announce rent transaction
- `PlayerBankruptEvent(playerId)` — Announce bankruptcy
- `GameEndedEvent(winnerId)` — Game is over

---

## Data Flow Example

### Scenario: Player buys Boardwalk

1. **Client** sends `BuyProperty("Boardwalk")`
2. **GameHub** validates player is current player and on Boardwalk
3. **GameHub** calls `GameEngine.BuyProperty(boardwalkProperty)`
4. **GameEngine** checks:
    - Is property unowned? ✓
    - Does player have $400? ✓
5. **GameEngine** modifies `GameState`:
    - `player.Cash -= 400`
    - `boardwalkProperty.OwnerId = player.Id`
    - `gameState.LogAction("Player bought Boardwalk")`
6. **GameHub** broadcasts `GameStateUpdated(gameState)` to all clients
7. **Clients** receive updated state and re-render board

---

## State Mutation Rules

**GameState** (data) is mutated by:
- `GameEngine` methods (primary)
- `GameRoomManager` for room operations

**GameState** is **read-only** to:
- `GameHub` (reads state, asks engine to mutate)
- Client UI (reads state, doesn't mutate)

**GameEngine** never stores state—it always receives a GameState instance and mutates it.

---

## Enums

### GameStatus
```
Waiting    — Game created, waiting for players
InProgress — Game has started
Finished   — Game ended (someone won)
Paused     — Game paused (not used initially)
```

### PropertyType
```
Street           — Purchasable property (varies by rent)
Railroad         — Purchasable railroad
Utility          — Electric Company or Water Works
Tax              — Income Tax or Luxury Tax
Chance           — Chance card
CommunityChest   — Community Chest card
FreeParking      — Free Parking (safe space)
GoToJail         — "Go to Jail" space
Jail             — Jail space (also used as corner)
Go               — "Go" corner with $200 collection
```

---
