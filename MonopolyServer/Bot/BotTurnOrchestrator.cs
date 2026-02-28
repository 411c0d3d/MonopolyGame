namespace MonopolyServer.Bot;

using Microsoft.AspNetCore.SignalR;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Hubs;
using MonopolyServer.Infrastructure;
using System.Collections.Concurrent;

/// <summary>
/// Orchestrates autonomous bot player turns. Executes all game actions in sequence with
/// humanizing delays. A per-game semaphore prevents concurrent bot execution.
/// Triggered externally via <see cref="TryScheduleIfBotTurn"/>.
/// </summary>
public class BotTurnOrchestrator
{
    private readonly GameRoomManager _roomManager;
    private readonly BotDecisionEngine _decisions;
    private readonly TradeService _tradeService;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<BotTurnOrchestrator> _logger;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gameLocks = new();

    private const int ActionDelayMs = 1000;
    private const int ShortDelayMs = 400;
    private const int LockTimeoutSeconds = 15;

    /// <summary>
    /// Constructor with all required services.
    /// </summary>
    public BotTurnOrchestrator(
        GameRoomManager roomManager,
        BotDecisionEngine decisions,
        TradeService tradeService,
        IHubContext<GameHub> hubContext,
        ILogger<BotTurnOrchestrator> logger)
    {
        _roomManager = roomManager;
        _decisions = decisions;
        _tradeService = tradeService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Schedules a bot turn if the current player is a bot. Safe to call redundantly —
    /// the semaphore prevents double execution.
    /// </summary>
    public void TryScheduleIfBotTurn(string gameId, GameState game)
    {
        var current = game.GetCurrentPlayer();
        if (current == null || !current.IsBot || current.IsBankrupt || game.Status != GameStatus.InProgress)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteBotTurnAsync(gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in bot turn for game {GameId}", gameId);
            }
        });
    }

    // -------------------------------------------------------------------------
    // Core turn loop
    // -------------------------------------------------------------------------

    private async Task ExecuteBotTurnAsync(string gameId)
    {
        var gameLock = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        if (!await gameLock.WaitAsync(TimeSpan.FromSeconds(LockTimeoutSeconds)))
        {
            _logger.LogWarning("Bot turn lock timed out for game {GameId}", gameId);
            return;
        }

        try
        {
            await Task.Delay(ActionDelayMs);

            if (!TryGetBotContext(gameId, out var game, out var engine, out var bot))
            {
                return;
            }

            // --- Jail handling ---
            if (bot.IsInJail)
            {
                bool stillInJail = await HandleJailAsync(gameId, game, engine, bot);
                await Task.Delay(ActionDelayMs);

                if (!TryGetBotContext(gameId, out game, out engine, out bot))
                {
                    return;
                }

                if (stillInJail || bot.IsInJail)
                {
                    // Failed escape roll — turn is over
                    engine.NextTurn();
                    await BroadcastAsync(gameId, game);
                    return;
                }

                // UseCard or PayBail — fall through to roll loop below
            }

            // --- Roll loop (handles consecutive doubles) ---
            await Task.Delay(ActionDelayMs);
            if (!TryGetBotContext(gameId, out game, out engine, out bot))
            {
                return;
            }

            if (game.Status != GameStatus.InProgress)
            {
                return;
            }

            bool keepRolling = true;
            while (keepRolling)
            {
                var (_, _, total, isDouble, sentToJail) = engine.RollDice();

                if (sentToJail)
                {
                    engine.NextTurn();
                    await BroadcastAsync(gameId, game);
                    return;
                }

                engine.MovePlayer(total);
                await BroadcastAsync(gameId, game);
                await Task.Delay(ShortDelayMs);

                if (!TryGetBotContext(gameId, out game, out engine, out bot))
                {
                    return;
                }

                if (game.Status != GameStatus.InProgress)
                {
                    return;
                }

                HandlePropertyDecision(game, engine, bot);

                keepRolling = isDouble && !bot.IsInJail;

                if (keepRolling)
                {
                    await Task.Delay(ActionDelayMs);
                    if (!TryGetBotContext(gameId, out game, out engine, out bot))
                    {
                        return;
                    }

                    if (game.Status != GameStatus.InProgress)
                    {
                        return;
                    }
                }
            }

            // --- Post-roll asset management ---
            if (!TryGetBotContext(gameId, out game, out engine, out bot))
            {
                return;
            }

            HandleProactiveMortgaging(game, engine, bot);
            HandleUnmortgaging(game, engine, bot);
            HandleBuilding(game, engine, bot);

            // --- Trade processing ---
            await HandleOutgoingTradesAsync(gameId, game, bot);
            HandleIncomingTrades(gameId, game, bot);

            // Broadcast final state before ending turn
            await BroadcastAsync(gameId, game);
            await Task.Delay(ShortDelayMs);

            // --- End turn ---
            engine.NextTurn();
            await BroadcastAsync(gameId, game);
        }
        finally
        {
            gameLock.Release();
        }
    }

    // -------------------------------------------------------------------------
    // Jail
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes the bot's jail strategy. Returns true if the bot is still in jail after this action
    /// (i.e. failed a doubles roll and the turn should end).
    /// </summary>
    private async Task<bool> HandleJailAsync(string gameId, GameState game, GameEngine engine, Player bot)
    {
        var strategy = _decisions.GetJailStrategy(bot);

        switch (strategy)
        {
            case JailStrategy.UseCard:
                engine.UseGetOutOfJailFreeCard(bot);
                await BroadcastAsync(gameId, game);
                return false;

            case JailStrategy.PayBail:
                engine.ReleaseFromJail(bot, payToBail: true);
                await BroadcastAsync(gameId, game);
                return false;

            default: // JailStrategy.Roll
                var (_, _, total, _, _) = engine.RollDice();
                bool escaped = engine.ReleaseFromJail(bot, payToBail: false);
                if (escaped)
                {
                    // Jail-escape doubles do not grant an extra turn
                    game.DoubleRolled = false;
                    engine.MovePlayer(total);
                    await BroadcastAsync(gameId, game);
                    await Task.Delay(ShortDelayMs);

                    if (TryGetBotContext(gameId, out var freshGame, out var freshEngine, out var freshBot))
                    {
                        HandlePropertyDecision(freshGame, freshEngine, freshBot);
                    }

                    return false;
                }

                await BroadcastAsync(gameId, game);
                return true; // Still in jail
        }
    }

    // -------------------------------------------------------------------------
    // Property decisions
    // -------------------------------------------------------------------------

    private void HandlePropertyDecision(GameState game, GameEngine engine, Player bot)
    {
        var property = game.Board.GetProperty(bot.Position);
        if (!IsPropertyBuyable(property))
        {
            return;
        }

        if (_decisions.ShouldBuyProperty(bot, property, game))
        {
            engine.BuyProperty(property);
        }
        else
        {
            engine.DeclineProperty(property);
        }
    }

    private static bool IsPropertyBuyable(Property property)
    {
        return property.OwnerId == null &&
               (property.Type == PropertyType.Street ||
                property.Type == PropertyType.Railroad ||
                property.Type == PropertyType.Utility);
    }

    // -------------------------------------------------------------------------
    // Mortgage management
    // -------------------------------------------------------------------------

    private void HandleProactiveMortgaging(GameState game, GameEngine engine, Player bot)
    {
        var candidates = game.Board.GetPropertiesByOwner(bot.Id)
            .Where(p => _decisions.ShouldMortgageProactively(bot, p))
            .ToList();

        foreach (var prop in candidates)
        {
            engine.ToggleMortgage(prop.Id);
        }
    }

    private void HandleUnmortgaging(GameState game, GameEngine engine, Player bot)
    {
        var candidates = game.Board.GetPropertiesByOwner(bot.Id)
            .Where(p => _decisions.ShouldUnmortgage(bot, p))
            .ToList();

        foreach (var prop in candidates)
        {
            engine.ToggleMortgage(prop.Id);
        }
    }

    // -------------------------------------------------------------------------
    // Building
    // -------------------------------------------------------------------------

    private void HandleBuilding(GameState game, GameEngine engine, Player bot)
    {
        var monopolies = game.Board.GetPropertiesByOwner(bot.Id)
            .Where(p => p.ColorGroup != null)
            .GroupBy(p => p.ColorGroup!)
            .Where(g =>
            {
                var fullGroup = game.Board.GetPropertiesByColorGroup(g.Key).ToList();
                return fullGroup.All(p => p.OwnerId == bot.Id);
            });

        foreach (var group in monopolies)
        {
            // Iterate until no more builds are possible in this group
            bool built = true;
            while (built)
            {
                built = false;
                foreach (var prop in group.OrderBy(p => p.HouseCount))
                {
                    if (_decisions.ShouldBuildHouse(bot, prop, game) && engine.BuildHouse(prop))
                    {
                        built = true;
                        break; // Re-sort after each build to maintain even-build rule
                    }

                    if (_decisions.ShouldBuildHotel(bot, prop, game) && engine.BuildHotel(prop))
                    {
                        built = true;
                        break;
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Trades
    // -------------------------------------------------------------------------

    private async Task HandleOutgoingTradesAsync(string gameId, GameState game, Player bot)
    {
        var proposals = _decisions.GenerateTradeProposals(bot, game);

        foreach (var proposal in proposals)
        {
            var result = _tradeService.ProposeTrade(gameId, bot.Id, proposal.ToPlayerId, proposal);
            if (result == null)
            {
                continue;
            }

            var target = game.GetPlayerById(proposal.ToPlayerId);
            if (target?.ConnectionId != null)
            {
                await _hubContext.Clients.Client(target.ConnectionId)
                    .SendAsync("TradeProposed", SerializeTradeOffer(result, game));
            }
        }
    }

    private void HandleIncomingTrades(string gameId, GameState game, Player bot)
    {
        var incoming = game.PendingTrades
            .Where(t => t.ToPlayerId == bot.Id && t.Status == TradeStatus.Pending)
            .ToList();

        foreach (var trade in incoming)
        {
            if (_decisions.ShouldAcceptTrade(trade, bot, game))
            {
                _tradeService.AcceptTrade(gameId, trade.Id, bot.Id);
            }
            else
            {
                _tradeService.RejectTrade(gameId, trade.Id, bot.Id);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to load current bot context. Returns false if the game is gone,
    /// finished, or the current player is no longer a live bot.
    /// </summary>
    private bool TryGetBotContext(
        string gameId,
        out GameState game,
        out GameEngine engine,
        out Player bot)
    {
        game = _roomManager.GetGame(gameId)!;
        engine = _roomManager.GetGameEngine(gameId)!;
        bot = null!;

        if (game.Status != GameStatus.InProgress)
        {
            return false;
        }

        bot = game.GetCurrentPlayer()!;
        return bot.IsBot && !bot.IsBankrupt;
    }

    private async Task BroadcastAsync(string gameId, GameState game)
    {
        await _roomManager.SaveGameAsync(gameId);
        await _hubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
    }

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
}