using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Hubs;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Background service that enforces turn time limits.
/// Warns the active player at 1 minute of inactivity, auto-rolls at 90 seconds, and skips the turn at 2 minutes.
/// </summary>
public class TurnTimerService : BackgroundService
{
    private readonly GameRoomManager _roomManager;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<TurnTimerService> _logger;

    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _warnThreshold = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _autoRollThreshold = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _turnTimeLimit = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Tracks which game+turn combos have already received a roll warning, to avoid repeat toasts.
    /// </summary>
    private readonly HashSet<string> _warnedTurns = new();

    /// <summary>
    /// Outcome of evaluating a single game's timer state.
    /// </summary>
    private enum TurnTimerAction
    {
        None,
        InitializeTurn,
        WarnPlayer,
        AutoRoll,
        ForceSkip
    }

    /// <summary>
    /// Constructor with dependency injection of GameRoomManager, IHubContext, and ILogger.
    /// </summary>
    public TurnTimerService(
        GameRoomManager roomManager,
        IHubContext<GameHub> hubContext,
        ILogger<TurnTimerService> logger)
    {
        _roomManager = roomManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Main loop — periodically checks all active games for turn timeouts until the service stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Turn timer service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTurnTimeouts();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking turn timeouts");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Turn timer service stopped");
    }

    /// <summary>
    /// Checks all in-progress games: initializes turn start time, warns at 1 min,
    /// auto-rolls at 90 s, and force-skips at 2 min.
    /// State mutations run inside ExecuteWithEngine (under lock); async I/O runs after.
    /// </summary>
    private async Task CheckTurnTimeouts()
    {
        var gameIds = _roomManager.GetAllGameIds();
        var activeTurnKeys = new HashSet<string>();

        foreach (var gameId in gameIds)
        {
            TurnTimerAction action = TurnTimerAction.None;
            string? playerName = null;
            string? playerId = null;
            string? connectionId = null;
            bool needsSave = false;

            _roomManager.ExecuteWithEngine(gameId, (game, engine) =>
            {
                if (game.Status != GameStatus.InProgress)
                {
                    return;
                }

                var currentPlayer = game.GetCurrentPlayer();
                if (currentPlayer == null)
                {
                    return;
                }

                var turnKey = $"{game.GameId}:{game.Turn}";
                activeTurnKeys.Add(turnKey);

                playerName = currentPlayer.Name;
                playerId = currentPlayer.Id;
                connectionId = currentPlayer.ConnectionId;

                if (game.CurrentTurnStartedAt == null)
                {
                    game.CurrentTurnStartedAt = DateTime.UtcNow;
                    action = TurnTimerAction.InitializeTurn;
                    needsSave = true;
                    return;
                }

                var elapsed = DateTime.UtcNow - game.CurrentTurnStartedAt.Value;

                if (elapsed > _turnTimeLimit)
                {
                    game.LogAction($"{currentPlayer.Name}'s turn timed out and was skipped.");
                    engine.NextTurn();
                    _warnedTurns.Remove(turnKey);
                    action = TurnTimerAction.ForceSkip;
                    needsSave = true;
                    return;
                }

                if (elapsed >= _autoRollThreshold && !currentPlayer.HasRolledDice)
                {
                    game.LogAction($"{currentPlayer.Name}'s dice were auto-rolled due to inactivity.");
                    var (_, _, total, _, sentToJail) = engine.RollDice();

                    if (sentToJail)
                    {
                        engine.NextTurn();
                    }
                    else
                    {
                        engine.MovePlayer(total);
                    }

                    action = TurnTimerAction.AutoRoll;
                    needsSave = true;
                    return;
                }

                if (elapsed >= _warnThreshold && !currentPlayer.HasRolledDice && !_warnedTurns.Contains(turnKey))
                {
                    _warnedTurns.Add(turnKey);
                    action = TurnTimerAction.WarnPlayer;
                }
            });

            // Async work (save + SignalR) happens outside the lock.
            if (needsSave)
            {
                await _roomManager.SaveGameAsync(gameId);
            }

            switch (action)
            {
                case TurnTimerAction.ForceSkip:
                    _logger.LogWarning("Turn timeout for {PlayerName} in game {GameId}", playerName, gameId);

                    await _hubContext.Clients.Group(gameId).SendAsync(
                        "TurnTimeout",
                        new { playerId, playerName });

                    await _hubContext.Clients.Group(gameId).SendAsync(
                        "GameStateUpdated",
                        await BuildGameStateDtoAsync(gameId));
                    break;

                case TurnTimerAction.AutoRoll:
                    _logger.LogInformation("Auto-rolled dice for {PlayerName} in game {GameId}", playerName, gameId);

                    await _hubContext.Clients.Group(gameId).SendAsync(
                        "GameStateUpdated",
                        await BuildGameStateDtoAsync(gameId));
                    break;

                case TurnTimerAction.WarnPlayer:
                    if (connectionId != null)
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync(
                            "TurnWarning",
                            new { message = "You haven't rolled yet — 30 seconds left before auto-roll!" });
                    }

                    break;
            }
        }

        // Prune stale warned-turn keys — any entry not seen in this cycle belongs to a finished or deleted game.
        _warnedTurns.IntersectWith(activeTurnKeys);
    }

    /// <summary>
    /// Reads the current game state under lock and maps it to a DTO for broadcast.
    /// </summary>
    private Task<object> BuildGameStateDtoAsync(string gameId)
    {
        // GetGame returns a live reference; we only read it here for mapping — no mutation.
        var game = _roomManager.GetGame(gameId);
        var dto = game != null ? GameStateMapper.ToDto(game) : (object)new { gameId };
        return Task.FromResult(dto);
    }
}