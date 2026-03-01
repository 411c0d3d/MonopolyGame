using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Bot;
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
    private readonly BotTurnOrchestrator _botOrchestrator;
    private readonly InputValidator _validator;
    private readonly ILogger<GameHub> _logger;

    /// <summary>
    /// Constructor with dependency injection for game room management, trade services, input validation, and logging.
    /// </summary>
    public GameHub(
        GameRoomManager roomManager,
        TradeService tradeService,
        BotTurnOrchestrator botOrchestrator,
        InputValidator validator,
        ILogger<GameHub> logger)
    {
        _roomManager = roomManager;
        _tradeService = tradeService;
        _botOrchestrator = botOrchestrator;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates and adds a player to a game session. Supports rejoining via name matching for persistent sessions.
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

                if (string.IsNullOrEmpty(game.HostId) || game.Players.Count == 1)
                {
                    game.HostId = player.Id;
                }
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

                if (string.IsNullOrEmpty(game.HostId) || game.Players.Count == 1)
                {
                    game.HostId = player.Id;
                }
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
    /// Triggers dice roll for the current player, moves them, and processes board landing logic.
    /// Jailed players must use HandleJail instead. On three consecutive doubles the turn auto-advances.
    /// </summary>
    public async Task RollDice(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
        if (!isValid || currentPlayer == null || game == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        if (currentPlayer.IsInJail)
        {
            await Clients.Caller.SendAsync("Error", "You are in jail — use HandleJail to roll for escape.");
            return;
        }

        // Block re-roll unless the previous roll was a double this turn
        if (currentPlayer.HasRolledDice && !game.DoubleRolled)
        {
            await Clients.Caller.SendAsync("Error", "You have already rolled this turn.");
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        var (_, _, total, _, sentToJail) = engine.RollDice();

        if (sentToJail)
        {
            // Three consecutive doubles — player goes to jail and their turn ends immediately
            engine.NextTurn();
        }
        else
        {
            engine.MovePlayer(total);
        }

        await PersistAndBroadcast(gameId, game);
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
            await PersistAndBroadcast(gameId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buying property in game {GameId}", gameId);
            await Clients.Caller.SendAsync("Error", "Failed to buy property");
        }
    }

    /// <summary>
    /// Builds a house on the specified property. Requires a monopoly and sufficient cash.
    /// </summary>
    public async Task BuildHouse(string gameId, int propertyId)
    {
        try
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
            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You do not own this property");
                return;
            }

            var engine = _roomManager.GetGameEngine(gameId);
            if (engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game engine not available");
                return;
            }

            if (!engine.BuildHouse(property))
            {
                await Clients.Caller.SendAsync("Error",
                    "Cannot build a house here — check monopoly ownership, house limit, or cash");
                return;
            }

            await PersistAndBroadcast(gameId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building house in game {GameId}", gameId);
            await Clients.Caller.SendAsync("Error", "Failed to build house");
        }
    }

    /// <summary>
    /// Builds a hotel on the specified property. Requires four houses and sufficient cash.
    /// </summary>
    public async Task BuildHotel(string gameId, int propertyId)
    {
        try
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
            if (property.OwnerId != currentPlayer.Id)
            {
                await Clients.Caller.SendAsync("Error", "You do not own this property");
                return;
            }

            var engine = _roomManager.GetGameEngine(gameId);
            if (engine == null)
            {
                await Clients.Caller.SendAsync("Error", "Game engine not available");
                return;
            }

            if (!engine.BuildHotel(property))
            {
                await Clients.Caller.SendAsync("Error", "Cannot build a hotel here — requires 4 houses or check cash");
                return;
            }

            await PersistAndBroadcast(gameId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building hotel in game {GameId}", gameId);
            await Clients.Caller.SendAsync("Error", "Failed to build hotel");
        }
    }

    /// <summary>
    /// Handles all jail actions: use a Get Out of Jail Free card, pay the $50 fine, or roll for escape.
    /// A successful roll (double) releases the player and moves them but does NOT grant an extra re-roll.
    /// On the third failed roll the engine force-releases the player and collects the bail via ForcePayment.
    /// </summary>
    public async Task HandleJail(string gameId, bool useCard, bool payFine)
    {
        var game = _roomManager.GetGame(gameId);
        var (isValid, currentPlayer, error) = VerifyCurrentPlayer(game);
        if (!isValid || currentPlayer == null || game == null)
        {
            await Clients.Caller.SendAsync("Error", error);
            return;
        }

        if (!currentPlayer.IsInJail)
        {
            await Clients.Caller.SendAsync("Error", "You are not in jail");
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
            if (!engine.UseGetOutOfJailFreeCard(currentPlayer))
            {
                await Clients.Caller.SendAsync("Error", "You do not have a Get Out of Jail Free card");
                return;
            }
            // Player is now free — they must still call RollDice to move this turn
        }
        else if (payFine)
        {
            engine.ReleaseFromJail(currentPlayer, payToBail: true);
            // Player is now free — they must still call RollDice to move this turn
        }
        else
        {
            // Roll attempt — engine.RollDice sets DoubleRolled before ReleaseFromJail reads it
            var (_, _, total, isDouble, _) = engine.RollDice();
            bool escaped = engine.ReleaseFromJail(currentPlayer, payToBail: false);

            if (escaped)
            {
                engine.MovePlayer(total);

                if (isDouble)
                {
                    // Jail-escape doubles do not grant an additional re-roll
                    game.DoubleRolled = false;
                }
            }
            // else: still in jail, player calls EndTurn to finish their turn
        }

        await PersistAndBroadcast(gameId, game);
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
        await PersistAndBroadcast(gameId, game);
    }

    /// <summary>
    /// Initiates a trade proposal between the caller and a target player.
    /// Bot targets are responded to automatically via the bot orchestrator.
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
        if (result == null)
        {
            _logger.LogDebug(
                "[BOT-TRADE] ProposeTrade returned null — trade was rejected by TradeService — gameId={GameId} fromId={FromId} toId={ToId}",
                gameId, caller.Id, toPlayerId);
            await Clients.Caller.SendAsync("Error",
                "Trade could not be created — check that you own the offered properties and have sufficient cash.");
            return;
        }

        var target = game.GetPlayerById(toPlayerId);

        _logger.LogDebug("[BOT-TRADE] Trade {TradeId} proposed — from='{FromName}' to='{ToName}' isBot={IsBot}",
            result.Id, caller.Name, target?.Name, target?.IsBot);

        // Persist before routing — bot reads from saved state, human gets a push notification
        await PersistAndBroadcast(gameId, game);

        if (target is { IsBot: true })
        {
            _logger.LogDebug("[BOT-TRADE] Routing to TryScheduleBotTradeResponse — botId={BotId}", toPlayerId);
            _botOrchestrator.TryScheduleBotTradeResponse(gameId, toPlayerId);
        }
        else if (target?.ConnectionId != null)
        {
            await Clients.Client(target.ConnectionId)
                .SendAsync("TradeProposed", SerializeTradeOffer(result, game));
        }
    }

    /// <summary>
    /// Responds to a pending trade request. Updates the board if the trade is accepted.
    /// </summary>
    public async Task RespondToTrade(string gameId, string tradeId, bool accept)
    {
        var game = _roomManager.GetGame(gameId);
        var caller = GetCallingPlayer(game);
        if (caller == null || game == null)
        {
            return;
        }

        bool success = accept
            ? _tradeService.AcceptTrade(gameId, tradeId, caller.Id)
            : _tradeService.RejectTrade(gameId, tradeId, caller.Id);

        if (success)
        {
            await PersistAndBroadcast(gameId, game);
        }
    }

    /// <summary>
    /// Ends the current player's turn and advances to the next non-bankrupt player.
    /// The player must have rolled this turn before ending (unless the timer forced an advance).
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

        if (!player.HasRolledDice)
        {
            await Clients.Caller.SendAsync("Error", "You must roll the dice before ending your turn.");
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
    /// Resigns the calling player from the game, triggering bankruptcy and asset transfer to the bank.
    /// </summary>
    public async Task ResignPlayer(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        var caller = GetCallingPlayer(game);
        if (caller == null)
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        if (game.Status != GameStatus.InProgress)
        {
            await Clients.Caller.SendAsync("Error", "Game is not in progress");
            return;
        }

        var engine = _roomManager.GetGameEngine(gameId);
        if (engine == null)
        {
            await Clients.Caller.SendAsync("Error", "Game engine not available");
            return;
        }

        engine.ResignPlayer(caller.Id);

        // If it was this player's turn, advance to the next player
        if (game.GetCurrentPlayer()?.Id == caller.Id || game.GetCurrentPlayer() == null)
        {
            engine.NextTurn();
        }

        await PersistAndBroadcast(gameId, game);
    }

    /// <summary>
    /// Allows the host to kick a player from the lobby or mark them bankrupt mid-game.
    /// </summary>
    public async Task KickPlayer(string gameId, string playerId)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            throw new HubException("Game not found");
        }

        var caller = GetCallingPlayer(game);
        if (caller == null || caller.Id != game.HostId)
        {
            throw new HubException("Only the host can kick players");
        }

        var target = game.Players.FirstOrDefault(p => p.Id == playerId)
                     ?? throw new HubException("Player not found");

        if (target.Id == caller.Id)
        {
            throw new HubException("Host cannot kick themselves");
        }

        if (game.Status == GameStatus.Waiting)
        {
            game.Players.Remove(target);
            game.LogAction($"{target.Name} was kicked by the host.");
        }
        else if (game.Status == GameStatus.InProgress)
        {
            var engine = _roomManager.GetGameEngine(gameId);
            if (engine != null)
            {
                engine.ResignPlayer(target.Id);
                if (game.GetCurrentPlayer()?.Id == target.Id || game.GetCurrentPlayer() == null)
                {
                    engine.NextTurn();
                }
            }
            else
            {
                target.IsBankrupt = true;
                target.IsConnected = false;
            }

            game.LogAction($"{target.Name} was removed by the host.");

            var activePlayers = game.Players.Where(p => !p.IsBankrupt).ToList();
            if (activePlayers.Count <= 1)
            {
                game.Status = GameStatus.Finished;
                game.FinishedAt = DateTime.UtcNow;
                game.WinnerId = activePlayers.FirstOrDefault()?.Id;
            }
        }
        else
        {
            throw new HubException("Cannot kick players in this game state");
        }

        if (target.ConnectionId != null)
        {
            await Clients.Client(target.ConnectionId).SendAsync("Kicked", "You were removed by the host");
        }

        _logger.LogInformation("Host {HostName} kicked player {PlayerName} from game {GameId}", caller.Name,
            target.Name, gameId);
        await PersistAndBroadcast(gameId, game);
    }

    /// <summary>
    /// Adds bots to a waiting game. Caller must be the host; no admin key required.
    /// </summary>
    public async Task AddBots(string gameId, int count = 1)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            throw new HubException("Game not found");
        }

        var caller = GetCallingPlayer(game);
        if (caller == null || caller.Id != game.HostId)
        {
            throw new HubException("Only the host can add bots");
        }

        if (game.Status != GameStatus.Waiting)
        {
            throw new HubException("Bots can only be added while the game is in the lobby");
        }

        int available = GameConstants.MaxPlayers - game.Players.Count;
        if (available <= 0)
        {
            throw new HubException("Lobby is full");
        }

        int toAdd = Math.Min(Math.Max(count, 1), available);

        int nextBotNumber = game.Players
            .Where(p => p.IsBot)
            .Select(p =>
            {
                var parts = p.Name.Split(' ');
                return parts.Length == 2 && int.TryParse(parts[1], out int n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max() + 1;

        for (int i = 0; i < toAdd; i++)
        {
            var botName = $"Bot {nextBotNumber++}";
            game.Players.Add(new Player(Guid.NewGuid().ToString(), botName)
            {
                IsBot = true,
                IsConnected = true,
                ConnectionId = null
            });
            game.LogAction($"{botName} joined the game as a bot.");
        }

        _logger.LogInformation("Host added {Count} bot(s) to game {GameId}", toAdd, gameId);
        await PersistAndBroadcast(gameId, game);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Persists game state, broadcasts to all players in the group,
    /// then schedules a bot turn if the next active player is a bot.
    /// </summary>
    private async Task PersistAndBroadcast(string gameId, GameState game)
    {
        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
        _botOrchestrator.TryScheduleIfBotTurn(gameId, game);
    }

    /// <summary>
    /// Validates that the SignalR caller is the authorized player for the current turn.
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
    /// Maps a GameState to its broadcast DTO via the shared mapper.
    /// </summary>
    private static GameStateDto SerializeGameState(GameState game) => GameStateMapper.ToDto(game);

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
    /// Resolves the calling player by their SignalR connection ID.
    /// </summary>
    private Player? GetCallingPlayer(GameState? game) =>
        game?.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
}