using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Hubs;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Background service that enforces turn time limits.
/// Automatically ends turns if players take too long.
/// </summary>
public class TurnTimerService : BackgroundService
{
    private readonly GameRoomManager _roomManager;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<TurnTimerService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _turnTimeLimit = TimeSpan.FromMinutes(3);

    public TurnTimerService(
        GameRoomManager roomManager,
        IHubContext<GameHub> hubContext,
        ILogger<TurnTimerService> logger)
    {
        _roomManager = roomManager;
        _hubContext = hubContext;
        _logger = logger;
    }

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
    /// Check all active games for turn timeouts and force end turn if needed.
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

            var turnDuration = DateTime.UtcNow - game.CurrentTurnStartedAt.Value;
            if (turnDuration > _turnTimeLimit)
            {
                _logger.LogWarning($"Turn timeout for {currentPlayer.Name} in game {game.GameId}");

                var engine = _roomManager.GetGameEngine(game.GameId);
                if (engine != null)
                {
                    game.LogAction($"{currentPlayer.Name}'s turn timed out.");
                    engine.NextTurn();
                    await _roomManager.SaveGameAsync(game.GameId);

                    await _hubContext.Clients.Group(game.GameId)
                        .SendAsync("TurnTimeout", new { playerId = currentPlayer.Id, playerName = currentPlayer.Name });
                }
            }
        }
    }
}