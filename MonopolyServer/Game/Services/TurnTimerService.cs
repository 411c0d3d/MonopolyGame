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

    /// <summary> Tracks which game+turn combos have already received a roll warning, to avoid repeat toasts. </summary>
    private readonly HashSet<string> _warnedTurns = new();

    /// <summary>
    /// Constructor with dependency injection of GameRoomManager for accessing game state, IHubContext for sending SignalR messages, and ILogger for logging turn timer actions.
    /// </summary>
    /// <param name="roomManager">The Game Room Manager.</param>
    /// <param name="hubContext">The Hub Context.</param>
    /// <param name="logger">The Logger.</param>
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
    /// Main loop that runs until the service is stopped. Periodically checks all active games for turn timeouts and takes appropriate actions (warn, auto-roll, skip).
    /// </summary>
    /// <param name="stoppingToken">The Cancellation Token</param>
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
    /// Checks all active games: initialises turn start time, sends a roll warning at 1 min,
    /// auto-rolls at 90 s, and force-skips the turn at 2 min.
    /// </summary>
    private async Task CheckTurnTimeouts()
    {
        var games = _roomManager.GetAllGames()
            .Where(g => g.Status == GameStatus.InProgress)
            .ToList();

        foreach (var game in games)
        {
            var currentPlayer = game.GetCurrentPlayer();
            if (currentPlayer == null)
            {
                continue;
            }

            if (game.CurrentTurnStartedAt == null)
            {
                game.CurrentTurnStartedAt = DateTime.UtcNow;
                await _roomManager.SaveGameAsync(game.GameId);
                continue;
            }

            var elapsed = DateTime.UtcNow - game.CurrentTurnStartedAt.Value;
            var turnKey = $"{game.GameId}:{game.Turn}";
            var hasRolled = currentPlayer.HasRolledDice;

            // ── 2 min: skip turn ─────────────────────────────────────────────────
            if (elapsed > _turnTimeLimit)
            {
                _warnedTurns.Remove(turnKey);
                await ForceSkipTurn(game, currentPlayer);
                continue;
            }

            // ── 90 s: auto-roll if still hasn't rolled ───────────────────────────
            if (elapsed >= _autoRollThreshold && !hasRolled)
            {
                await AutoRollDice(game, currentPlayer);
                continue;
            }

            // ── 60 s: warn once if still hasn't rolled ───────────────────────────
            if (elapsed >= _warnThreshold && !hasRolled && !_warnedTurns.Contains(turnKey))
            {
                _warnedTurns.Add(turnKey);

                if (currentPlayer.ConnectionId != null)
                {
                    await _hubContext.Clients.Client(currentPlayer.ConnectionId).SendAsync(
                        "TurnWarning",
                        new { message = "You haven't rolled yet — 30 seconds left before auto-roll!" });
                }
            }
        }
    }

    /// <summary>
    /// Auto-rolls and moves the current player, then broadcasts updated state.
    /// </summary>
    private async Task AutoRollDice(GameState game, Player currentPlayer)
    {
        var engine = _roomManager.GetGameEngine(game.GameId);
        if (engine == null)
        {
            return;
        }

        _logger.LogInformation(
            "Auto-rolling dice for {PlayerName} in game {GameId}",
            currentPlayer.Name, game.GameId);

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

        await _roomManager.SaveGameAsync(game.GameId);

        await _hubContext.Clients.Group(game.GameId).SendAsync(
            "GameStateUpdated",
            GameStateMapper.ToDto(game));
    }

    /// <summary>
    /// Forces the current turn to end and broadcasts the new state.
    /// </summary>
    private async Task ForceSkipTurn(GameState game,
        Player currentPlayer)
    {
        var engine = _roomManager.GetGameEngine(game.GameId);
        if (engine == null)
        {
            return;
        }

        _logger.LogWarning(
            "Turn timeout for {PlayerName} in game {GameId}",
            currentPlayer.Name, game.GameId);

        game.LogAction($"{currentPlayer.Name}'s turn timed out and was skipped.");
        engine.NextTurn();
        await _roomManager.SaveGameAsync(game.GameId);

        await _hubContext.Clients.Group(game.GameId).SendAsync(
            "TurnTimeout",
            new { playerId = currentPlayer.Id, playerName = currentPlayer.Name });

        // Broadcast updated state so all clients advance to the new player's turn
        await _hubContext.Clients.Group(game.GameId).SendAsync(
            "GameStateUpdated",
            GameStateMapper.ToDto(game));
    }
}