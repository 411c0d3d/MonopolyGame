using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Bot;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Hubs;

/// <summary>
/// SignalR Hub for real-time Monopoly game communication.
/// All game state mutations go through GameRoomManager.MutateGame / InitializeEngine / ExecuteWithEngine
/// to ensure they run under the global lock. Persistence and broadcasts always happen outside the lock.
/// </summary>
public class GameHub : Hub
{
    private readonly GameRoomManager _roomManager;
    private readonly TradeService _tradeService;
    private readonly BotTurnOrchestrator _botOrchestrator;
    private readonly InputValidator _validator;
    private readonly ILogger<GameHub> _logger;

    /// <summary>Constructor with dependency injection for game room management, trade services, input validation, and logging.</summary>
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

    /// <summary>Authenticates and adds a player to a game session. Supports rejoining via name matching.</summary>
    public async Task JoinGame(string gameId, string playerName)
    {
        try
        {
            var (isGameIdValid, gameIdError) = _validator.ValidateGameId(gameId);
            if (!isGameIdValid)
            {
                await SendError(nameof(JoinGame), gameId, gameIdError!);
                return;
            }

            playerName = _validator.SanitizePlayerName(playerName);
            var (isNameValid, nameError) = _validator.ValidatePlayerName(playerName);
            if (!isNameValid)
            {
                await SendError(nameof(JoinGame), gameId, nameError!);
                return;
            }

            if (_roomManager.GetGame(gameId) == null)
            {
                await SendError(nameof(JoinGame), gameId, "Game not found");
                return;
            }

            string? errorMsg = null;

            var found = _roomManager.MutateGame(gameId, (g, _) =>
            {
                var existing = g.Players.FirstOrDefault(p => p.Name == playerName);
                if (existing != null)
                {
                    existing.ConnectionId = Context.ConnectionId;
                    existing.IsConnected = true;
                    if (string.IsNullOrEmpty(g.HostId))
                    {
                        g.HostId = existing.Id;
                    }

                    return;
                }

                if (g.Status != GameStatus.Waiting)
                {
                    errorMsg = "You are not a participant in this game";
                    return;
                }

                if (g.Players.Count >= GameConstants.MaxPlayers)
                {
                    errorMsg = "Game is full";
                    return;
                }

                var player = new Player(Guid.NewGuid().ToString(), playerName)
                    { ConnectionId = Context.ConnectionId, IsConnected = true };
                g.Players.Add(player);
                if (string.IsNullOrEmpty(g.HostId))
                {
                    g.HostId = player.Id;
                }
            });

            if (!found)
            {
                await SendError(nameof(JoinGame), gameId, "Game vanished before join could complete");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(JoinGame), gameId, errorMsg);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(JoinGame), gameId);
        }
    }

    /// <summary>Transitions the game from the lobby to an active state.</summary>
    public async Task StartGame(string gameId)
    {
        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(StartGame), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;

        var found = _roomManager.InitializeEngine(gameId, (g, engine) =>
        {
            if (g.Status != GameStatus.Waiting)
            {
                errorMsg = "Game already started";
                return;
            }

            if (g.Players.Count < 2)
            {
                errorMsg = "Need at least 2 players to start";
                return;
            }

            engine.StartGame();
        });

        if (!found)
        {
            await SendError(nameof(StartGame), gameId, "Game vanished before start could complete");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(StartGame), gameId, errorMsg);
            return;
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Triggers dice roll for the current player, moves them, and processes board landing logic.
    /// Jailed players must use HandleJail instead.
    /// </summary>
    public async Task RollDice(string gameId)
    {
        string? errorMsg = null;
        int d1 = 0, d2 = 0;
        bool sentToJail = false;
        int total = 0;
        Card? drawnCard = null;

        var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
        {
            var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
            if (!valid)
            {
                errorMsg = err;
                return;
            }

            if (caller!.IsInJail)
            {
                errorMsg = "You are in jail — use HandleJail to roll for escape.";
                return;
            }

            if (caller.HasRolledDice && !g.DoubleRolled)
            {
                errorMsg = "You have already rolled this turn.";
                return;
            }

            (d1, d2, total, _, sentToJail) = engine.RollDice();

            if (sentToJail)
            {
                // Three consecutive doubles — player goes to jail and their turn ends immediately
                engine.NextTurn();
            }
            else
            {
                drawnCard = engine.MovePlayer(total);
            }
        });

        if (!found)
        {
            await SendError(nameof(RollDice), gameId, "Game or engine not available");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(RollDice), gameId, errorMsg);
            return;
        }

        await Clients.Group(gameId).SendAsync("DiceRolled", d1, d2);

        if (drawnCard != null)
        {
            await Clients.Group(gameId).SendAsync("CardDrawn", new
            {
                type = drawnCard.DeckType.ToString(),
                text = drawnCard.Title,
                amount = drawnCard.Amount
            });
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>Executes a property purchase for the current player on their current tile.</summary>
    public async Task BuyProperty(string gameId)
    {
        try
        {
            string? errorMsg = null;

            var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
            {
                var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
                if (!valid)
                {
                    errorMsg = err;
                    return;
                }

                var property = g.Board.GetProperty(caller!.Position);
                if (property.Type != PropertyType.Street &&
                    property.Type != PropertyType.Railroad &&
                    property.Type != PropertyType.Utility)
                {
                    errorMsg = "This space is not purchasable";
                    return;
                }

                engine.BuyProperty(property);
            });

            if (!found)
            {
                await SendError(nameof(BuyProperty), gameId, "Game or engine not available");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(BuyProperty), gameId, errorMsg);
                return;
            }

            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(BuyProperty), gameId);
            await Clients.Caller.SendAsync("Error", "Failed to buy property");
        }
    }

    /// <summary>Builds a house on the specified property. Requires a monopoly and sufficient cash.</summary>
    public async Task BuildHouse(string gameId, int propertyId)
    {
        try
        {
            var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
            if (!isPropValid)
            {
                await SendError(nameof(BuildHouse), gameId, propErr!);
                return;
            }

            string? errorMsg = null;

            var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
            {
                var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
                if (!valid)
                {
                    errorMsg = err;
                    return;
                }

                var property = g.Board.GetProperty(propertyId);
                if (property.OwnerId != caller!.Id)
                {
                    errorMsg = "You do not own this property";
                    return;
                }

                if (!engine.BuildHouse(property))
                {
                    errorMsg = "Cannot build a house here — check monopoly ownership, house limit, or cash";
                }
            });

            if (!found)
            {
                await SendError(nameof(BuildHouse), gameId, "Game or engine not available");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(BuildHouse), gameId, errorMsg);
                return;
            }

            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(BuildHouse), gameId);
            await Clients.Caller.SendAsync("Error", "Failed to build house");
        }
    }

    /// <summary>Builds a hotel on the specified property. Requires four houses and sufficient cash.</summary>
    public async Task BuildHotel(string gameId, int propertyId)
    {
        try
        {
            var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
            if (!isPropValid)
            {
                await SendError(nameof(BuildHotel), gameId, propErr!);
                return;
            }

            string? errorMsg = null;

            var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
            {
                var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
                if (!valid)
                {
                    errorMsg = err;
                    return;
                }

                var property = g.Board.GetProperty(propertyId);
                if (property.OwnerId != caller!.Id)
                {
                    errorMsg = "You do not own this property";
                    return;
                }

                if (!engine.BuildHotel(property))
                {
                    errorMsg = "Cannot build a hotel here — requires 4 houses or check cash";
                }
            });

            if (!found)
            {
                await SendError(nameof(BuildHotel), gameId, "Game or engine not available");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(BuildHotel), gameId, errorMsg);
                return;
            }

            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(BuildHotel), gameId);
            await Clients.Caller.SendAsync("Error", "Failed to build hotel");
        }
    }

    /// <summary>Sells the hotel on the specified property, returning 50% of hotel cost to the player.</summary>
    public async Task SellHotel(string gameId, int propertyId)
    {
        try
        {
            var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
            if (!isPropValid)
            {
                await SendError(nameof(SellHotel), gameId, propErr!);
                return;
            }

            string? errorMsg = null;

            var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
            {
                var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
                if (!valid)
                {
                    errorMsg = err;
                    return;
                }

                var property = g.Board.GetProperty(propertyId);
                if (property.OwnerId != caller!.Id)
                {
                    errorMsg = "You do not own this property";
                    return;
                }

                if (!property.HasHotel)
                {
                    errorMsg = "This property does not have a hotel";
                    return;
                }

                if (!engine.SellHotel(property))
                {
                    errorMsg = "Cannot sell hotel on this property";
                }
            });

            if (!found)
            {
                await SendError(nameof(SellHotel), gameId, "Game or engine not available");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(SellHotel), gameId, errorMsg);
                return;
            }

            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(SellHotel), gameId);
            await Clients.Caller.SendAsync("Error", "Failed to sell hotel");
        }
    }

    /// <summary>Sells one house from the specified property, returning 50% of house cost to the player.</summary>
    public async Task SellHouse(string gameId, int propertyId)
    {
        try
        {
            var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
            if (!isPropValid)
            {
                await SendError(nameof(SellHouse), gameId, propErr!);
                return;
            }

            string? errorMsg = null;

            var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
            {
                var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
                if (!valid)
                {
                    errorMsg = err;
                    return;
                }

                var property = g.Board.GetProperty(propertyId);
                if (property.OwnerId != caller!.Id)
                {
                    errorMsg = "You do not own this property";
                    return;
                }

                if (property.HasHotel)
                {
                    errorMsg = "Sell the hotel before selling houses";
                    return;
                }

                if (property.HouseCount == 0)
                {
                    errorMsg = "This property has no houses to sell";
                    return;
                }

                if (!engine.SellHouse(property))
                {
                    errorMsg = "Cannot sell house on this property";
                }
            });

            if (!found)
            {
                await SendError(nameof(SellHouse), gameId, "Game or engine not available");
                return;
            }

            if (errorMsg != null)
            {
                await SendError(nameof(SellHouse), gameId, errorMsg);
                return;
            }

            await PersistAndBroadcast(gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Method}] Unhandled exception — gameId={GameId}", nameof(SellHouse), gameId);
            await Clients.Caller.SendAsync("Error", "Failed to sell house");
        }
    }

    /// <summary>
    /// Handles all jail actions: use a Get Out of Jail Free card, pay the $50 fine, or roll for escape.
    /// A successful roll (double) releases the player and moves them but does NOT grant an extra re-roll.
    /// On the third failed roll the engine force-releases the player and collects the bail via ForcePayment.
    /// </summary>
    public async Task HandleJail(string gameId, bool useCard, bool payFine)
    {
        string? errorMsg = null;

        var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
        {
            var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
            if (!valid)
            {
                errorMsg = err;
                return;
            }

            if (!caller!.IsInJail)
            {
                errorMsg = "You are not in jail";
                return;
            }

            if (useCard)
            {
                if (!engine.UseGetOutOfJailFreeCard(caller))
                {
                    // Player is now free — they must still call RollDice to move this turn
                    errorMsg = "You do not have a Get Out of Jail Free card";
                }
            }
            else if (payFine)
            {
                
                engine.ReleaseFromJail(caller, payToBail: true);
                // Player is now free — they must still call RollDice to move this turn
            }
            else
            {
                // Roll attempt — engine.RollDice sets DoubleRolled before ReleaseFromJail reads it
                var (dice1, dice2, total, isDouble, sentToJail) = engine.RollDice();
                bool escaped = engine.ReleaseFromJail(caller, payToBail: false);
                if (escaped)
                {
                    engine.MovePlayer(total);
                    if (isDouble)
                    {
                        // Jail-escape doubles do not grant an additional re-roll
                        g.DoubleRolled = false;
                    }
                }
            }
        });

        if (!found)
        {
            await SendError(nameof(HandleJail), gameId, "Game or engine not available");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(HandleJail), gameId, errorMsg);
            return;
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Toggles the mortgage status of a property.
    /// </summary>
    public async Task ToggleMortgage(string gameId, int propertyId)
    {
        var (isPropValid, propErr) = _validator.ValidatePropertyId(propertyId);
        if (!isPropValid)
        {
            await SendError(nameof(ToggleMortgage), gameId, propErr!);
            return;
        }

        string? errorMsg = null;

        var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
        {
            var (valid, caller, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
            if (!valid)
            {
                errorMsg = err;
                return;
            }

            var property = g.Board.GetProperty(propertyId);
            if (property.OwnerId != caller!.Id)
            {
                errorMsg = "You don't own this property";
                return;
            }

            engine.ToggleMortgage(propertyId);
        });

        if (!found)
        {
            await SendError(nameof(ToggleMortgage), gameId, "Game or engine not available");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(ToggleMortgage), gameId, errorMsg);
            return;
        }

        await PersistAndBroadcast(gameId);
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
            await SendError(nameof(ProposeTrade), gameId, "Unauthorized");
            return;
        }

        var (isCashValid, cashError) = _validator.ValidateTradeCash(offer.OfferedCash);
        if (!isCashValid)
        {
            await SendError(nameof(ProposeTrade), gameId, cashError!);
            return;
        }

        var (isReqCashValid, reqCashError) = _validator.ValidateTradeCash(offer.RequestedCash);
        if (!isReqCashValid)
        {
            await SendError(nameof(ProposeTrade), gameId, reqCashError!);
            return;
        }

        var (arePropsValid, propsError) = _validator.ValidateTradeProperties(offer.OfferedPropertyIds);
        if (!arePropsValid)
        {
            await SendError(nameof(ProposeTrade), gameId, propsError!);
            return;
        }

        var (areReqPropsValid, reqPropsError) = _validator.ValidateTradeProperties(offer.RequestedPropertyIds);
        if (!areReqPropsValid)
        {
            await SendError(nameof(ProposeTrade), gameId, reqPropsError!);
            return;
        }

        var result = _tradeService.ProposeTrade(gameId, caller.Id, toPlayerId, offer);
        if (result == null)
        {
            _logger.LogWarning(
                "[{Method}] TradeService rejected trade — gameId={GameId} fromId={FromId} toId={ToId}",
                nameof(ProposeTrade), gameId, caller.Id, toPlayerId);
            await Clients.Caller.SendAsync("Error",
                "Trade could not be created — check that you own the offered properties and have sufficient cash.");
            return;
        }

        var target = game.GetPlayerById(toPlayerId);
        _logger.LogDebug("[BOT-TRADE] Trade {TradeId} proposed — from='{FromName}' to='{ToName}' isBot={IsBot}",
            result.Id, caller.Name, target?.Name, target?.IsBot);

        await PersistAndBroadcast(gameId);

        if (target is { IsBot: true })
        {
            _botOrchestrator.TryScheduleBotTradeResponse(gameId, toPlayerId);
        }
        else if (target?.ConnectionId != null)
        {
            var fresh = _roomManager.GetGame(gameId);
            if (fresh != null)
            {
                await Clients.Client(target.ConnectionId)
                    .SendAsync("TradeProposed", SerializeTradeOffer(result, fresh));
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
        if (caller == null || game == null)
        {
            _logger.LogWarning("[{Method}] Caller not found in game — gameId={GameId}", nameof(RespondToTrade), gameId);
            return;
        }

        bool success = accept
            ? _tradeService.AcceptTrade(gameId, tradeId, caller.Id)
            : _tradeService.RejectTrade(gameId, tradeId, caller.Id);

        if (!success)
        {
            _logger.LogWarning(
                "[{Method}] Trade response failed — gameId={GameId} tradeId={TradeId} accept={Accept} playerId={PlayerId}",
                nameof(RespondToTrade), gameId, tradeId, accept, caller.Id);
            return;
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Ends the current player's turn and advances to the next non-bankrupt player.
    /// The player must have rolled this turn before ending (unless the timer forced an advance).
    /// </summary>
    public async Task EndTurn(string gameId)
    {
        string? errorMsg = null;

        var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
        {
            var (valid, player, err) = VerifyCurrentPlayer(g, Context.ConnectionId);
            if (!valid)
            {
                errorMsg = err;
                return;
            }

            if (!player!.HasRolledDice)
            {
                errorMsg = "You must roll the dice before ending your turn.";
                return;
            }

            if (g.Players.Where(p => !p.IsBankrupt).Count() <= 1)
            {
                errorMsg = "Game has ended";
                return;
            }

            engine.NextTurn();
        });

        if (!found)
        {
            await SendError(nameof(EndTurn), gameId, "Game or engine not available");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(EndTurn), gameId, errorMsg);
            return;
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Marks the disconnecting player as offline and persists the state change.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var game = _roomManager.GetAllGames()
            .FirstOrDefault(g => g.Players.Any(p => p.ConnectionId == Context.ConnectionId));

        if (game != null)
        {
            var playerId = game.Players.First(p => p.ConnectionId == Context.ConnectionId).Id;
            _roomManager.MarkPlayerDisconnected(playerId);
            await PersistAndBroadcast(game.GameId);
        }

        if (exception != null)
        {
            _logger.LogWarning(exception, "Client disconnected with error — connectionId={ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Resigns the calling player from the game, triggering bankruptcy and asset transfer to the bank.
    /// </summary>
    public async Task ResignPlayer(string gameId)
    {
        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(ResignPlayer), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;

        var found = _roomManager.ExecuteWithEngine(gameId, (g, engine) =>
        {
            var caller = GetCallingPlayer(g);
            if (caller == null)
            {
                errorMsg = "Unauthorized";
                return;
            }

            if (g.Status != GameStatus.InProgress)
            {
                errorMsg = "Game is not in progress";
                return;
            }

            engine.ResignPlayer(caller.Id);

            if (g.GetCurrentPlayer()?.Id == caller.Id || g.GetCurrentPlayer() == null)
            {
                engine.NextTurn();
            }
        });

        if (!found)
        {
            await SendError(nameof(ResignPlayer), gameId, "Game or engine not available");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(ResignPlayer), gameId, errorMsg);
            return;
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Allows the host to kick a player from the lobby or mark them bankrupt mid-game.
    /// </summary>
    public async Task KickPlayer(string gameId, string playerId)
    {
        if (_roomManager.GetGame(gameId) == null)
        {
            _logger.LogWarning("[{Method}] Game not found — gameId={GameId}", nameof(KickPlayer), gameId);
            throw new HubException("Game not found");
        }

        string? kickedConnectionId = null;
        string? errorMsg = null;

        var found = _roomManager.MutateGame(gameId, (g, engine) =>
        {
            var caller = GetCallingPlayer(g);
            if (caller == null || caller.Id != g.HostId)
            {
                errorMsg = "Only the host can kick players";
                return;
            }

            var target = g.Players.FirstOrDefault(p => p.Id == playerId);
            if (target == null)
            {
                errorMsg = "Player not found";
                return;
            }

            if (target.Id == caller.Id)
            {
                errorMsg = "Host cannot kick themselves";
                return;
            }

            kickedConnectionId = target.ConnectionId;

            if (g.Status == GameStatus.Waiting)
            {
                g.Players.Remove(target);
                g.LogAction($"{target.Name} was kicked by the host.");
            }
            else if (g.Status == GameStatus.InProgress)
            {
                if (engine != null)
                {
                    engine.ResignPlayer(target.Id);
                    if (g.GetCurrentPlayer()?.Id == target.Id || g.GetCurrentPlayer() == null)
                    {
                        engine.NextTurn();
                    }
                }
                else
                {
                    target.IsBankrupt = true;
                    target.IsConnected = false;
                }

                g.LogAction($"{target.Name} was removed by the host.");

                var activePlayers = g.Players.Where(p => !p.IsBankrupt).ToList();
                if (activePlayers.Count <= 1)
                {
                    g.Status = GameStatus.Finished;
                    g.FinishedAt = DateTime.UtcNow;
                    g.WinnerId = activePlayers.FirstOrDefault()?.Id;
                }
            }
            else
            {
                errorMsg = "Cannot kick players in this game state";
            }
        });

        if (!found || errorMsg != null)
        {
            var reason = !found ? "Game vanished before kick could complete" : errorMsg!;
            _logger.LogWarning("[{Method}] Kick failed — gameId={GameId} playerId={PlayerId} reason={Reason}",
                nameof(KickPlayer), gameId, playerId, reason);
            throw new HubException(reason);
        }

        if (kickedConnectionId != null)
        {
            await Clients.Client(kickedConnectionId).SendAsync("Kicked", "You were removed by the host");
        }

        await PersistAndBroadcast(gameId);
    }

    /// <summary>
    /// Adds bots to a waiting game. Caller must be the host.
    /// </summary>
    public async Task AddBots(string gameId, int count = 1)
    {
        if (_roomManager.GetGame(gameId) == null)
        {
            _logger.LogWarning("[{Method}] Game not found — gameId={GameId}", nameof(AddBots), gameId);
            throw new HubException("Game not found");
        }

        string? errorMsg = null;

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            var caller = GetCallingPlayer(g);
            if (caller == null || caller.Id != g.HostId)
            {
                errorMsg = "Only the host can add bots";
                return;
            }

            if (g.Status != GameStatus.Waiting)
            {
                errorMsg = "Bots can only be added while the game is in the lobby";
                return;
            }

            int available = GameConstants.MaxPlayers - g.Players.Count;
            if (available <= 0)
            {
                errorMsg = "Lobby is full";
                return;
            }

            int toAdd = Math.Min(Math.Max(count, 1), available);
            int nextBotNumber = g.Players
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
                g.Players.Add(new Player(Guid.NewGuid().ToString(), botName)
                    { IsBot = true, IsConnected = true, ConnectionId = null });
                g.LogAction($"{botName} joined the game as a bot.");
            }

            _logger.LogInformation("Host added {Count} bot(s) to game {GameId}", toAdd, gameId);
        });

        if (!found || errorMsg != null)
        {
            var reason = !found ? "Game vanished before bots could be added" : errorMsg!;
            _logger.LogWarning("[{Method}] AddBots failed — gameId={GameId} reason={Reason}",
                nameof(AddBots), gameId, reason);
            throw new HubException(reason);
        }

        await PersistAndBroadcast(gameId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends an error to the caller and logs a warning with method and game context.
    /// </summary>
    private async Task SendError(string method, string gameId, string message)
    {
        _logger.LogWarning("[{Method}] {Message} — gameId={GameId} connectionId={ConnectionId}",
            method, message, gameId, Context.ConnectionId);
        await Clients.Caller.SendAsync("Error", message);
    }

    /// <summary>
    /// Persists game state, broadcasts to all players in the group, then schedules a bot turn if applicable.
    /// </summary>
    private async Task PersistAndBroadcast(string gameId)
    {
        await _roomManager.SaveGameAsync(gameId);
        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            _logger.LogWarning("[PersistAndBroadcast] Game not found after save — gameId={GameId}", gameId);
            return;
        }

        await Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
        _botOrchestrator.TryScheduleIfBotTurn(gameId, game);
    }

    /// <summary>
    /// Validates that the SignalR caller is the authorized player for the current turn.
    /// </summary>
    private static (bool isValid, Player? player, string? error) VerifyCurrentPlayer(
        GameState game, string connectionId)
    {
        var caller = game.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
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
    /// Maps a trade offer to its DTO for broadcasting.
    /// </summary>
    private static TradeOfferDto SerializeTradeOffer(TradeOffer t, GameState g) => new()
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