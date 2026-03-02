using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Hubs;

namespace MonopolyServer.Infrastructure;

/// <summary>
/// Background service that periodically cleans up:
/// - Abandoned games (0 players for > 1 hour)
/// - Finished games (completed > 7 days ago)
/// - Disconnected players (offline > 10 minutes)
/// </summary>
public class GameCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GameCleanupService> _logger;

    /// <summary>
    /// Constructor with dependency injection of IServiceProvider and ILogger.
    /// </summary>
    public GameCleanupService(IServiceProvider serviceProvider, ILogger<GameCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game cleanup service started");

        RunStartupCleanup();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(GameConstants.CleanupIntervalMinutes), stoppingToken);
                await PerformCleanup();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during game cleanup");
            }
        }

        _logger.LogInformation("Game cleanup service stopped");
    }

    /// <summary>
    /// Purges all finished games from disk and memory immediately on service start.
    /// </summary>
    private void RunStartupCleanup()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var roomManager = scope.ServiceProvider.GetRequiredService<GameRoomManager>();
            roomManager.VacuumStorage(g => g.Status == GameStatus.Finished);
            _logger.LogInformation("Startup cleanup: all finished games purged");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup cleanup");
        }
    }

    private async Task PerformCleanup()
    {
        using var scope = _serviceProvider.CreateScope();
        var roomManager = scope.ServiceProvider.GetRequiredService<GameRoomManager>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

        var now = DateTime.UtcNow;
        var allGames = roomManager.GetAllGames();
        var gamesCleanedUp = 0;
        var playersRemoved = 0;

        foreach (var game in allGames)
        {
            if (game.Players.Count == 0 &&
                now - game.CreatedAt > TimeSpan.FromHours(GameConstants.AbandonedGameCleanupHours))
            {
                roomManager.DeleteGame(game.GameId);
                gamesCleanedUp++;
                _logger.LogInformation("Cleaned up abandoned game: {GameId}", game.GameId);
                continue;
            }

            if (game.Status == GameStatus.Finished &&
                game.FinishedAt.HasValue &&
                now - game.FinishedAt.Value > TimeSpan.FromDays(GameConstants.FinishedGameCleanupDays))
            {
                roomManager.DeleteGame(game.GameId);
                gamesCleanedUp++;
                _logger.LogInformation("Cleaned up finished game: {GameId}", game.GameId);
                continue;
            }

            var disconnectedPlayers = game.Players
                .Where(p => !p.IsConnected &&
                            p.DisconnectedAt.HasValue &&
                            now - p.DisconnectedAt.Value >
                            TimeSpan.FromMinutes(GameConstants.PlayerDisconnectTimeoutMinutes))
                .ToList();

            if (disconnectedPlayers.Count == 0)
            {
                continue;
            }

            bool stateChanged = false;
            bool gameDeleted = false;

            if (game.Status == GameStatus.Waiting)
            {
                roomManager.MutateGame(game.GameId, (g, _) =>
                {
                    foreach (var player in disconnectedPlayers)
                    {
                        g.Players.Remove(player);
                        g.LogAction($"{player.Name} was removed due to extended disconnection.");
                        playersRemoved++;
                        stateChanged = true;

                        if (g.Players.Count == 0)
                        {
                            roomManager.DeleteGame(g.GameId);
                            gameDeleted = true;
                            gamesCleanedUp++;
                            _logger.LogInformation("Game {GameId} deleted after all players disconnected", g.GameId);
                            return;
                        }

                        if (g.HostId == player.Id)
                        {
                            g.HostId = g.Players[0].Id;
                            _logger.LogInformation("New host assigned in game {GameId}", g.GameId);
                        }
                    }
                });
            }
            else if (game.Status == GameStatus.InProgress)
            {
                roomManager.MutateGame(game.GameId, (g, engine) =>
                {
                    if (engine == null)
                    {
                        return;
                    }

                    foreach (var player in disconnectedPlayers)
                    {
                        if (player.IsBankrupt)
                        {
                            continue;
                        }

                        var currentPlayer = g.GetCurrentPlayer();
                        bool wasCurrentPlayer = currentPlayer?.Id == player.Id;

                        engine.ResignPlayer(player.Id);
                        g.LogAction($"{player.Name} was bankrupted due to extended disconnection.");
                        playersRemoved++;
                        stateChanged = true;

                        _logger.LogInformation("Player {PlayerName} bankrupted in game {GameId} due to disconnect",
                            player.Name, g.GameId);

                        if (wasCurrentPlayer)
                        {
                            var activePlayers = g.Players.Where(p => !p.IsBankrupt).ToList();
                            if (activePlayers.Count > 0)
                            {
                                engine.NextTurn();
                                _logger.LogInformation("Turn advanced in game {GameId} after current player disconnect",
                                    g.GameId);
                            }
                        }

                        if (g.HostId == player.Id)
                        {
                            var activePlayer = g.Players.FirstOrDefault(p => !p.IsBankrupt);
                            if (activePlayer != null)
                            {
                                g.HostId = activePlayer.Id;
                                _logger.LogInformation("New host assigned in game {GameId}", g.GameId);
                            }
                        }
                    }
                });
            }

            if (stateChanged && !gameDeleted)
            {
                await roomManager.SaveGameAsync(game.GameId);
                await BroadcastGameState(hubContext, game);
            }
        }

        if (gamesCleanedUp > 0 || playersRemoved > 0)
        {
            _logger.LogInformation(
                "Cleanup completed: {GamesRemoved} games removed, {PlayersAffected} players affected",
                gamesCleanedUp, playersRemoved);
        }
    }

    /// <summary>
    /// Broadcasts game state to all clients in a group using the shared typed DTO.
    /// </summary>
    private static async Task BroadcastGameState(IHubContext<GameHub> hubContext, GameState game)
    {
        var dto = GameStateMapper.ToDto(game);
        await hubContext.Clients.Group(game.GameId).SendAsync("GameStateUpdated", dto);
    }
}