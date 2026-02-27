using Microsoft.AspNetCore.SignalR;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Hubs;

/// <summary>
/// SignalR Hub for real-time Monopoly game communication.
/// Handles persistent player sessions, game state orchestration, and validated input processing.
/// </summary>
public class GameHub : Hub
{
    private readonly GameRoomManager _roomManager;
    private readonly TradeService _tradeService;
    private readonly InputValidator _validator;
    private readonly ILogger<GameHub> _logger;

    /// <summary>
    /// Constructor with dependency injection for game room management, trade services, input validation, and logging.
    /// </summary>
    /// <param name="roomManager"></param>
    /// <param name="tradeService"></param>
    /// <param name="validator"></param>
    /// <param name="logger"></param>
    public GameHub(
        GameRoomManager roomManager,
        TradeService tradeService,
        InputValidator validator,
        ILogger<GameHub> logger)
    {
        _roomManager = roomManager;
        _tradeService = tradeService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates and adds a player to a game session. 
    /// Supports rejoining via name matching for persistent sessions.
    /// </summary>
    public async Task JoinGame(string gameId, string playerName)
    {
        try
        {
            var (isGameIdValid, gameIdError) = _validator.ValidateGameId(gameId);
            if (!isGameIdValid)
            {
                await Clients.Caller.SendAsync("Error", gameIdError);
                return;
            }

            playerName = _validator.SanitizePlayerName(playerName);
            var (isNameValid, nameError) = _validator.ValidatePlayerName(playerName);
            if (!isNameValid)
            {
                await Clients.Caller.SendAsync("Error", nameError);
                return;
            }

            var game = _roomManager.GetGame(gameId);
            if (game == null)
            {
                await Clients.Caller.SendAsync("Error", "Game not found");
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.Name == playerName);
            if (player != null)
            {
                player.ConnectionId = Context.ConnectionId;
                player.IsConnected = true;
            }
            else if (game.Status == GameStatus.Waiting)
            {
                if (game.Players.Count >= GameConstants.MaxPlayers)
                {
                    await Clients.Caller.SendAsync("Error", "Game is full");
                    return;
                }

                player = new Player(Guid.NewGuid().ToString(), playerName)
                    { ConnectionId = Context.ConnectionId, IsConnected = true };
                game.Players.Add(player);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
            await PersistAndBroadcast(gameId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in JoinGame for {GameId}", gameId);
        }
    }

    /// <summary>
    /// Transitions the game from the lobby to an active state.
    /// </summary>
// Transitions the game from the lobby to an active state.
    public async Task StartGame(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        if (game.Status != GameStatus.Waiting)
        {
            await Clients.Caller.SendAsync("Error", "Game already started");
            return;
        }

        if (game.Players.Count < 2)
        {
            await Clients.Caller.SendAsync("Error", "Need at least 2 players to start");
            return;
        }

        // Initialize the engine if it doesn't exist yet
        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            engine = new GameEngine(game);
            _roomManager.SetGameEngine(gameId, engine);
        }

        engine.StartGame();
        await PersistAndBroadcast(gameId, game);
    }

    /// <summary>
    /// Triggers a dice roll, moves the player, and processes board landing logic.
    /// </summary>
    public async Task RollDice(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
        if (!isValid || currentPlayer == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        var (dice1, dice2, total, isDouble, sentToJail) = engine.RollDice();
        
        if (!sentToJail)
        {
            if (currentPlayer.IsInJail)
            {
                if (isDouble)
                {
                    engine.ReleaseFromJail(currentPlayer, payToBail: false);
                    engine.MovePlayer(total);
                }
            }
            else
            {
                engine.MovePlayer(total);
            }
        }

        await PersistAndBroadcast(gameId, game!);
    }

    /// <summary>
    /// Executes a property purchase for the current player on their current tile.
    /// </summary>
    public async Task BuyProperty(string gameId)
    {
        try
        {
            var game = _roomManager.GetGame(gameId);
            var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
            if (!isValid || currentPlayer == null || game == null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return;
            }

            var property = game.Board.GetProperty(currentPlayer.Position);
            if (property == null)
            {
                await Clients.Caller.SendAsync("Error", "Cannot buy this space");
                return;
            }

            if (property.Type != PropertyType.Street && 
                property.Type != PropertyType.Railroad && 
                property.Type != PropertyType.Utility)
            {
                await Clients.Caller.SendAsync("Error", "This space is not purchasable");
                return;
            }

            var engine = _roomManager.GetGameEngine(gameId);
            if (engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game engine not available");
                return;
            }

            engine.BuyProperty(property);
            await PersistAndBroadcast(gameId, game!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buying property");
            await Clients.Caller.SendAsync("Error", "Failed to buy property");
        }
    }

    /// <summary>
    /// Handles specialized logic for players attempting to exit the Jail space.
    /// </summary>
    public async Task HandleJail(string gameId, bool useCard, bool payFine)
    {
        var game = _roomManager.GetGame(gameId);
        var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
        if (!isValid || currentPlayer == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        if (useCard)
        {
            engine.UseGetOutOfJailFreeCard(currentPlayer);
        }
        else if (payFine)
        {
            engine.PayBailToLeaveJail(currentPlayer);
        }
        else
        {
            var (dice1, dice2, total, isDouble, sentToJail) = engine.RollDice();
            
            if (isDouble)
            {
                engine.ReleaseFromJail(currentPlayer, payToBail: false);
                engine.MovePlayer(total);
            }
            else
            {
                currentPlayer.JailTurnsRemaining--;
                if (currentPlayer.JailTurnsRemaining <= 0)
                {
                    engine.PayBailToLeaveJail(currentPlayer);
                }
            }
        }

        await PersistAndBroadcast(gameId, game!);
    }

    /// <summary>
    /// Toggles the mortgage status of a property. Validates property ID before processing.
    /// </summary>
    public async Task ToggleMortgage(string gameId, int propertyId)
    {
        var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
        if (!isPropValid)
        {
            await Clients.Caller.SendAsync("Error", propErr);
            return;
        }

        var game = _roomManager.GetGame(gameId);
        var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
        if (!isValid || currentPlayer == null || game == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        var property = game.Board.GetProperty(propertyId);
        if (property == null)
        {
            await Clients.Caller.SendAsync("Error", "Property not found");
            return;
        }

        if (property.OwnerId != currentPlayer.Id)
        {
            await Clients.Caller.SendAsync("Error", "You don't own this property");
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        engine.ToggleMortgage(propertyId);
        await PersistAndBroadcast(gameId, game!);
    }

    /// <summary>
    /// Initiates a trade proposal between the caller and a target player.
    /// </summary>
    public async Task ProposeTrade(string gameId, string toPlayerId, TradeOffer offer)
    {
        var game = _roomManager.GetGame(gameId);
        var caller = GetCallingPlayer(game);
        if (caller == null || game == null)
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var (isCashValid, cashError) = _validator.ValidateTradeCash(offer.OfferedCash);
        if (!isCashValid)
        {
            await Clients.Caller.SendAsync("Error", cashError);
            return;
        }

        var (isReqCashValid, reqCashError) = _validator.ValidateTradeCash(offer.RequestedCash);
        if (!isReqCashValid)
        {
            await Clients.Caller.SendAsync("Error", reqCashError);
            return;
        }

        var (arePropsValid, propsError) = _validator.ValidateTradeProperties(offer.OfferedPropertyIds);
        if (!arePropsValid)
        {
            await Clients.Caller.SendAsync("Error", propsError);
            return;
        }

        var (areReqPropsValid, reqPropsError) = _validator.ValidateTradeProperties(offer.RequestedPropertyIds);
        if (!areReqPropsValid)
        {
            await Clients.Caller.SendAsync("Error", reqPropsError);
            return;
        }

        var result = _tradeService.ProposeTrade(gameId, caller.Id, toPlayerId, offer);
        if (result != null)
        {
            var target = game.GetPlayerById(toPlayerId);
            if (target?.ConnectionId != null)
            {
                await Clients.Client(target.ConnectionId).SendAsync("TradeProposed", SerializeTradeOffer(result, game));
            }
        }
    }

    /// <summary>
    /// Responds to a pending trade request. Updates the board if the trade is accepted.
    /// </summary>
    public async Task RespondToTrade(string gameId, string tradeId, bool accept)
    {
        var game = _roomManager.GetGame(gameId);
        var caller = GetCallingPlayer(game);
        if (caller == null)
        {
            return;
        }

        bool success = accept
            ? _tradeService.AcceptTrade(gameId, tradeId, caller.Id)
            : _tradeService.RejectTrade(gameId, tradeId, caller.Id);

        if (success)
        {
            await PersistAndBroadcast(gameId, game!);
        }
    }

    /// <summary>
    /// Ends the current turn and rotates play to the next non-bankrupt player.
    /// </summary>
    public async Task EndTurn(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        var (isValid, player, error) = VerifyCurrentPlayer(game);
        if (!isValid || game == null || player == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        var activePlayers = game.Players.Where(p => !p.IsBankrupt).ToList();
        if (activePlayers.Count <= 1)
        {
            await Clients.Caller.SendAsync("Error", "Game has ended");
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        engine.NextTurn();
        await PersistAndBroadcast(gameId, game);
    }

    /// <summary>
    /// Marks players as disconnected and records the timestamp for potential cleanup or timeouts.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var game = _roomManager.GetAllGames()
            .FirstOrDefault(g => g.Players.Any(p => p.ConnectionId == Context.ConnectionId));
        if (game != null)
        {
            var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
            player.IsConnected = false;
            player.DisconnectedAt = DateTime.UtcNow;
            await PersistAndBroadcast(game.GameId, game);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Centralized persistence and broadcast logic to ensure state consistency.
    /// </summary>
    private async Task PersistAndBroadcast(string gameId, GameState game)
    {
        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("GameStateUpdated", SerializeGameState(game));
    }

    /// <summary>
    /// Validates that the caller is the authorized player for the current turn.
    /// </summary>
    private (bool isValid, Player? player, string? error) VerifyCurrentPlayer(GameState? game)
    {
        if (game == null)
        {
            return (false, null, "Game not found");
        }

        var caller = GetCallingPlayer(game);
        if (caller == null)
        {
            return (false, null, "Unauthorized");
        }

        if (game.Status != GameStatus.InProgress)
        {
            return (false, caller, "Game not running");
        }

        if (game.GetCurrentPlayer()?.Id != caller.Id)
        {
            return (false, caller, "Not your turn");
        }

        return (true, caller, null);
    }

    /// <summary>
    /// Maps internal state into a clean DTO for client consumption.
    /// </summary>
    private GameStateDto SerializeGameState(GameState game) => new()
    {
        GameId = game.GameId,
        Status = game.Status.ToString(),
        Turn = game.Turn,
        CurrentPlayerIndex = game.CurrentPlayerIndex,
        CurrentPlayer = game.GetCurrentPlayer() != null ? MapToDto(game.GetCurrentPlayer()!) : null,
        Players = game.Players.Select(MapToDto).ToList(),
        Board = game.Board.Spaces.Select(s => new PropertyDto
        {
            Id = s.Id,
            Name = s.Name,
            OwnerId = s.OwnerId,
            OwnerName = s.OwnerId != null ? game.GetPlayerById(s.OwnerId)?.Name : null,
            HouseCount = s.HouseCount,
            HasHotel = s.HasHotel,
            IsMortgaged = s.IsMortgaged
        }).ToList(),
        EventLog = game.EventLog.TakeLast(10).ToList()
    };

    /// <summary>
    /// Maps player model to DTO.
    /// </summary>
    private PlayerDto MapToDto(Player p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Cash = p.Cash,
        Position = p.Position,
        IsInJail = p.IsInJail,
        IsBankrupt = p.IsBankrupt,
        KeptCardCount = p.KeptCards.Count,
        IsConnected = p.IsConnected,
        DisconnectedAt = p.DisconnectedAt
    };

    /// <summary>
    /// Maps a trade offer to a DTO for broadcasting.
    /// </summary>
    private TradeOfferDto SerializeTradeOffer(TradeOffer t, GameState g) => new()
    {
        Id = t.Id,
        FromPlayerId = t.FromPlayerId,
        FromPlayerName = g.GetPlayerById(t.FromPlayerId)?.Name ?? "Unknown",
        ToPlayerId = t.ToPlayerId,
        ToPlayerName = g.GetPlayerById(t.ToPlayerId)?.Name ?? "Unknown",
        OfferedCash = t.OfferedCash,
        RequestedCash = t.RequestedCash,
        OfferedPropertyIds = t.OfferedPropertyIds,
        RequestedPropertyIds = t.RequestedPropertyIds,
        Status = t.Status.ToString()
    };

    /// <summary>
    /// Helper to retrieve the player model using the SignalR Context.
    /// </summary>
    private Player? GetCallingPlayer(GameState? game) =>
        game?.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
}