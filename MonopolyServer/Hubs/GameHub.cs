using Microsoft.AspNetCore.SignalR;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;

namespace MonopolyServer.Hubs;

/// <summary>
/// SignalR Hub for real-time Monopoly game communication.
/// Handles client requests and broadcasts game state updates to all connected players.
/// </summary>
public class GameHub : Hub
{
    private readonly GameRoomManager _roomManager;
    private readonly TradeService _tradeService;
    private readonly ILogger<GameHub> _logger;

    public GameHub(GameRoomManager roomManager, TradeService tradeService, ILogger<GameHub> logger)
    {
        _roomManager = roomManager;
        _tradeService = tradeService;
        _logger = logger;
    }

    /// <summary>
    /// Client calls this to join an existing game room.
    /// </summary>
    public async Task JoinGame(string gameId, string playerName)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            if (game.Status != GameStatus.Waiting)
            {
                await Clients.Caller.SendAsync("Error", "Game has already started");
                return;
            }

            if (game.Players.Count >= 4)
            {
                await Clients.Caller.SendAsync("Error", "Game is full");
                return;
            }

            var player = new Player(Context.ConnectionId, playerName);
            game.Players.Add(player);
            game.LogAction($"{playerName} joined the game.");

            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

            _logger.LogInformation($"Player {playerName} joined game {gameId}");

            await Clients.Group(gameId).SendAsync("PlayerJoined", new
            {
                playerId = player.Id,
                playerName = player.Name,
                playerCount = game.Players.Count
            });

            await Clients.Caller.SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining game");
            await Clients.Caller.SendAsync("Error", "Failed to join game");
        }
    }

    /// <summary>
    /// Client calls this to create a new game room.
    /// Host becomes the first player.
    /// </summary>
    public async Task CreateGame(string playerName)
    {
        try
        {
            var gameId = _roomManager.CreateGame();
            var game = _roomManager.GetGame(gameId)!;

            var hostPlayer = new Player(Context.ConnectionId, playerName);
            game.HostId = hostPlayer.Id;
            game.Players.Add(hostPlayer);
            game.LogAction($"{playerName} created the game.");

            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

            _logger.LogInformation($"Game {gameId} created by {playerName}");

            await Clients.Caller.SendAsync("GameCreated", new
            {
                gameId,
                playerId = hostPlayer.Id
            });

            await Clients.Caller.SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating game");
            await Clients.Caller.SendAsync("Error", "Failed to create game");
        }
    }

    /// <summary>
    /// Host calls this to start the game once all players are ready.
    /// </summary>
    public async Task StartGame(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            if (game.HostId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Only the host can start the game");
                return;
            }

            if (game.Players.Count < 2)
            {
                await Clients.Caller.SendAsync("Error", "Need at least 2 players to start");
                return;
            }

            var engine = new GameEngine(game);
            _roomManager.SetGameEngine(gameId, engine);
            engine.StartGame();

            _logger.LogInformation($"Game {gameId} started with {game.Players.Count} players");

            await Clients.Group(gameId).SendAsync("GameStarted", new
            {
                message = "Game started!",
                currentPlayer = game.GetCurrentPlayer()!.Name,
                turn = game.Turn
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting game");
            await Clients.Caller.SendAsync("Error", "Failed to start game");
        }
    }

    /// <summary>
    /// Current player rolls the dice.
    /// </summary>
    public async Task RollDice(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            if (currentPlayer.HasRolledDice)
            {
                await Clients.Caller.SendAsync("Error", "You've already rolled this turn");
                return;
            }

            var (dice1, dice2, total, isDouble) = engine.RollDice();

            _logger.LogInformation($"Player {currentPlayer.Name} rolled: {dice1} + {dice2} = {total}");

            await Clients.Group(gameId).SendAsync("DiceRolled", new
            {
                playerName = currentPlayer.Name,
                dice1,
                dice2,
                total,
                isDouble
            });

            if (currentPlayer.IsInJail && !isDouble)
            {
                engine.ReleaseFromJail(currentPlayer);

                if (!currentPlayer.IsInJail)
                {
                    await Clients.Group(gameId).SendAsync("PlayerReleasedFromJail", new
                    {
                        playerName = currentPlayer.Name
                    });
                }
            }

            engine.MovePlayer(total);

            await Clients.Group(gameId).SendAsync("PlayerMoved", new
            {
                playerName = currentPlayer.Name,
                newPosition = currentPlayer.Position,
                propertyName = game.Board.GetProperty(currentPlayer.Position).Name
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling dice");
            await Clients.Caller.SendAsync("Error", "Failed to roll dice");
        }
    }

    /// <summary>
    /// Current player buys the property they're standing on.
    /// </summary>
    public async Task BuyProperty(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            var property = game.Board.GetProperty(currentPlayer.Position);

            if (property.OwnerId != null)
            {
                await Clients.Caller.SendAsync("Error", "Property is already owned");
                return;
            }

            if (!engine.BuyProperty(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot afford this property");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} bought {property.Name}");

            await Clients.Group(gameId).SendAsync("PropertyBought", new
            {
                playerName = currentPlayer.Name,
                propertyName = property.Name,
                price = property.PurchasePrice
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buying property");
            await Clients.Caller.SendAsync("Error", "Failed to buy property");
        }
    }

    /// <summary>
    /// Current player draws a Chance card.
    /// </summary>
    public async Task DrawChanceCard(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            engine.DrawAndExecuteCard(CardDeck.Chance);

            await Clients.Group(gameId).SendAsync("CardDrawn", new
            {
                playerName = currentPlayer.Name,
                deckType = "Chance"
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error drawing Chance card");
            await Clients.Caller.SendAsync("Error", "Failed to draw card");
        }
    }

    /// <summary>
    /// Current player draws a Community Chest card.
    /// </summary>
    public async Task DrawCommunityChestCard(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            engine.DrawAndExecuteCard(CardDeck.CommunityChest);

            await Clients.Group(gameId).SendAsync("CardDrawn", new
            {
                playerName = currentPlayer.Name,
                deckType = "Community Chest"
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error drawing Community Chest card");
            await Clients.Caller.SendAsync("Error", "Failed to draw card");
        }
    }

    /// <summary>
    /// Current player builds a house on one of their properties.
    /// </summary>
    public async Task BuildHouse(string gameId, int propertyId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            var property = game.Board.GetProperty(propertyId);

            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You don't own this property");
                return;
            }

            if (!engine.BuildHouse(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot build on this property");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} built a house on {property.Name}");

            await Clients.Group(gameId).SendAsync("HouseBuilt", new
            {
                playerName = currentPlayer.Name,
                propertyName = property.Name,
                houseCount = property.HouseCount
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building house");
            await Clients.Caller.SendAsync("Error", "Failed to build house");
        }
    }

    /// <summary>
    /// Current player builds a hotel on one of their properties.
    /// </summary>
    public async Task BuildHotel(string gameId, int propertyId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            var property = game.Board.GetProperty(propertyId);

            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You don't own this property");
                return;
            }

            if (!engine.BuildHotel(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot build hotel on this property");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} built a hotel on {property.Name}");

            await Clients.Group(gameId).SendAsync("HotelBuilt", new
            {
                playerName = currentPlayer.Name,
                propertyName = property.Name
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building hotel");
            await Clients.Caller.SendAsync("Error", "Failed to build hotel");
        }
    }

    /// <summary>
    /// Current player mortgages one of their properties.
    /// </summary>
    public async Task MortgageProperty(string gameId, int propertyId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            var property = game.Board.GetProperty(propertyId);

            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You don't own this property");
                return;
            }

            if (!engine.MortgageProperty(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot mortgage this property");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} mortgaged {property.Name}");

            await Clients.Group(gameId).SendAsync("PropertyMortgaged", new
            {
                playerName = currentPlayer.Name,
                propertyName = property.Name,
                mortgageValue = property.MortgageValue
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mortgaging property");
            await Clients.Caller.SendAsync("Error", "Failed to mortgage property");
        }
    }

    /// <summary>
    /// Current player unmortgages one of their properties.
    /// </summary>
    public async Task UnmortgageProperty(string gameId, int propertyId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            var property = game.Board.GetProperty(propertyId);

            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You don't own this property");
                return;
            }

            if (!engine.UnmortgageProperty(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot unmortgage this property");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} unmortgaged {property.Name}");

            await Clients.Group(gameId).SendAsync("PropertyUnmortgaged", new
            {
                playerName = currentPlayer.Name,
                propertyName = property.Name
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmortgaging property");
            await Clients.Caller.SendAsync("Error", "Failed to unmortgage property");
        }
    }

    /// <summary>
    /// Current player ends their turn and advances to the next player.
    /// </summary>
    public async Task EndTurn(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            engine.NextTurn();

            var nextPlayer = game.GetCurrentPlayer();
            _logger.LogInformation($"Turn ended. Next player: {nextPlayer?.Name}");

            await Clients.Group(gameId).SendAsync("TurnEnded", new
            {
                previousPlayer = currentPlayer.Name,
                nextPlayer = nextPlayer?.Name,
                turn = game.Turn
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending turn");
            await Clients.Caller.SendAsync("Error", "Failed to end turn");
        }
    }

    /// <summary>
    /// Player uses a Get Out of Jail Free card to leave jail.
    /// </summary>
    public async Task UseGetOutOfJailFreeCard(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var engine = _roomManager.GetGameEngine(gameId);

            if (game == null || engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer?.Id != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Not your turn");
                return;
            }

            if (!engine.UseGetOutOfJailFreeCard(currentPlayer))
            {
                await Clients.Caller.SendAsync("Error", "You don't have a Get Out of Jail Free card");
                return;
            }

            _logger.LogInformation($"Player {currentPlayer.Name} used Get Out of Jail Free card");

            await Clients.Group(gameId).SendAsync("PlayerUsedJailFreeCard", new
            {
                playerName = currentPlayer.Name
            });

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error using jail free card");
            await Clients.Caller.SendAsync("Error", "Failed to use card");
        }
    }

    /// <summary>
    /// Any player proposes a trade to another player (can happen anytime, not just their turn).
    /// </summary>
    public async Task ProposeTrade(string gameId, string toPlayerId, TradeOfferDto tradeDto)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var proposingPlayer = game.GetPlayerById(Context.ConnectionId);
            if (proposingPlayer == null)
            {
                await Clients.Caller.SendAsync("Error", "Player not found");
                return;
            }

            var tradeOffer = new TradeOffer(
                Context.ConnectionId,
                toPlayerId,
                tradeDto.OfferedPropertyIds,
                tradeDto.OfferedCash,
                tradeDto.OfferedCardIds,
                tradeDto.RequestedPropertyIds,
                tradeDto.RequestedCash,
                tradeDto.RequestedCardIds
            );

            var result = _tradeService.ProposeTrade(gameId, Context.ConnectionId, toPlayerId, tradeOffer);
            if (result == null)
            {
                await Clients.Caller.SendAsync("Error", "Invalid trade proposal");
                return;
            }

            // Notify the receiving player
            await Clients.User(toPlayerId).SendAsync("TradeProposed", SerializeTradeOffer(result, game));

            // Notify proposing player
            await Clients.Caller.SendAsync("TradeProposalSent", SerializeTradeOffer(result, game));

            await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proposing trade");
            await Clients.Caller.SendAsync("Error", "Failed to propose trade");
        }
    }

    /// <summary>
    /// Receiving player accepts a trade offer.
    /// </summary>
    public async Task AcceptTrade(string gameId, string tradeId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            if (!_tradeService.AcceptTrade(gameId, tradeId, Context.ConnectionId))
            {
                await Clients.Caller.SendAsync("Error", "Cannot accept this trade");
                return;
            }

            var trade = _tradeService.GetTrade(gameId, tradeId);
            if (trade != null)
            {
                var acceptingPlayer = game.GetPlayerById(Context.ConnectionId);
                var proposingPlayer = game.GetPlayerById(trade.FromPlayerId);

                await Clients.Group(gameId).SendAsync("TradeAccepted", new
                {
                    tradeId = tradeId,
                    acceptingPlayerName = acceptingPlayer?.Name,
                    proposingPlayerName = proposingPlayer?.Name
                });

                await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting trade");
            await Clients.Caller.SendAsync("Error", "Failed to accept trade");
        }
    }

    /// <summary>
    /// Receiving player rejects a trade offer.
    /// </summary>
    public async Task RejectTrade(string gameId, string tradeId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            if (!_tradeService.RejectTrade(gameId, tradeId, Context.ConnectionId))
            {
                await Clients.Caller.SendAsync("Error", "Cannot reject this trade");
                return;
            }

            var trade = _tradeService.GetTrade(gameId, tradeId);
            if (trade != null)
            {
                var rejectingPlayer = game.GetPlayerById(Context.ConnectionId);
                var proposingPlayer = game.GetPlayerById(trade.FromPlayerId);

                await Clients.Group(gameId).SendAsync("TradeRejected", new
                {
                    tradeId = tradeId,
                    rejectingPlayerName = rejectingPlayer?.Name,
                    proposingPlayerName = proposingPlayer?.Name
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting trade");
            await Clients.Caller.SendAsync("Error", "Failed to reject trade");
        }
    }

    /// <summary>
    /// Proposing player cancels a pending trade offer.
    /// </summary>
    public async Task CancelTrade(string gameId, string tradeId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            if (!_tradeService.CancelTrade(gameId, tradeId, Context.ConnectionId))
            {
                await Clients.Caller.SendAsync("Error", "Cannot cancel this trade");
                return;
            }

            var trade = _tradeService.GetTrade(gameId, tradeId);
            if (trade != null)
            {
                var cancelingPlayer = game.GetPlayerById(Context.ConnectionId);
                var receivingPlayer = game.GetPlayerById(trade.ToPlayerId);

                await Clients.Group(gameId).SendAsync("TradeCancelled", new
                {
                    tradeId = tradeId,
                    cancelingPlayerName = cancelingPlayer?.Name,
                    receivingPlayerName = receivingPlayer?.Name
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling trade");
            await Clients.Caller.SendAsync("Error", "Failed to cancel trade");
        }
    }

    /// <summary>
    /// Get the current game state (called when client reconnects or needs refresh).
    /// </summary>
    public async Task GetGameState(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            await Clients.Caller.SendAsync("GameStateUpdated", SerializeGameState(game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting game state");
            await Clients.Caller.SendAsync("Error", "Failed to get game state");
        }
    }

    /// <summary>
    /// Handle client disconnect—remove player from game.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client {Context.ConnectionId} disconnected");

        var game = _roomManager.GetGameByPlayerId(Context.ConnectionId);
        if (game != null)
        {
            var playerToRemove = game.Players.FirstOrDefault(p => p.Id == Context.ConnectionId);
            if (playerToRemove != null)
            {
                game.Players.Remove(playerToRemove);
                game.LogAction($"{playerToRemove.Name} left the game.");

                if (game.Players.Count == 0)
                {
                    _roomManager.DeleteGame(game.GameId);
                    _logger.LogInformation($"Game {game.GameId} deleted (no players)");
                }
                else
                {
                    await Clients.Group(game.GameId).SendAsync("PlayerLeft", new
                    {
                        playerCount = game.Players.Count
                    });

                    if (game.HostId == Context.ConnectionId && game.Players.Count > 0)
                    {
                        game.HostId = game.Players[0].Id;
                        _logger.LogInformation($"Game {game.GameId}: new host assigned");
                    }
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Serialize game state to send to clients.
    /// </summary>
    public GameStateDto SerializeGameState(GameState game)
    {
        return new GameStateDto
        {
            GameId = game.GameId,
            Status = game.Status.ToString(),
            Turn = game.Turn,
            CurrentPlayerIndex = game.CurrentPlayerIndex,
            CurrentPlayer = game.GetCurrentPlayer() != null
                ? new PlayerDto
                {
                    Id = game.GetCurrentPlayer()!.Id,
                    Name = game.GetCurrentPlayer()!.Name,
                    Cash = game.GetCurrentPlayer()!.Cash,
                    Position = game.GetCurrentPlayer()!.Position,
                    IsInJail = game.GetCurrentPlayer()!.IsInJail,
                    IsBankrupt = game.GetCurrentPlayer()!.IsBankrupt,
                    KeptCardCount = game.GetCurrentPlayer()!.KeptCards.Count
                }
                : null,
            Players = game.Players.Select(p => new PlayerDto
            {
                Id = p.Id,
                Name = p.Name,
                Cash = p.Cash,
                Position = p.Position,
                IsInJail = p.IsInJail,
                IsBankrupt = p.IsBankrupt,
                KeptCardCount = p.KeptCards.Count
            }).ToList(),
            Board = game.Board.Spaces.Select(space => new PropertyDto
            {
                Id = space.Id,
                Name = space.Name,
                Type = space.Type.ToString(),
                Position = space.Position,
                OwnerId = space.OwnerId,
                OwnerName = space.OwnerId != null ? game.GetPlayerById(space.OwnerId)?.Name : null,
                HouseCount = space.HouseCount,
                HasHotel = space.HasHotel,
                IsMortgaged = space.IsMortgaged,
                ColorGroup = space.ColorGroup
            }).ToList(),
            GameLog = game.GameLog.TakeLast(20).ToList()
        };
    }

    /// <summary>
    /// Serialize a trade offer to send to clients.
    /// </summary>
    private TradeOfferDto SerializeTradeOffer(TradeOffer trade, GameState game)
    {
        var fromPlayer = game.GetPlayerById(trade.FromPlayerId);
        var toPlayer = game.GetPlayerById(trade.ToPlayerId);

        return new TradeOfferDto
        {
            Id = trade.Id,
            FromPlayerId = trade.FromPlayerId,
            FromPlayerName = fromPlayer?.Name ?? "Unknown",
            ToPlayerId = trade.ToPlayerId,
            ToPlayerName = toPlayer?.Name ?? "Unknown",
            OfferedPropertyIds = trade.OfferedPropertyIds,
            OfferedCash = trade.OfferedCash,
            OfferedCardIds = trade.OfferedCardIds,
            RequestedPropertyIds = trade.RequestedPropertyIds,
            RequestedCash = trade.RequestedCash,
            RequestedCardIds = trade.RequestedCardIds,
            Status = trade.Status.ToString(),
            CreatedAt = trade.CreatedAt
        };
    }
}