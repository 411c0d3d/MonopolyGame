using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Hubs;

namespace MonopolyServer.Infrastructure;

/// <summary>
/// Background service that periodically cleans up:
/// - Abandoned games (0 players for > configured hours)
/// - Finished games (completed > configured days ago)
/// - Disconnected players (offline > configured minutes)
/// </summary>
public class GameCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GameCleanupService> _logger;

    /// <summary>Constructor with dependency injection of IServiceProvider and ILogger.</summary>
    public GameCleanupService(IServiceProvider serviceProvider, ILogger<GameCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game cleanup service started");

        await RunStartupCleanupAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(GameConstants.CleanupIntervalMinutes), stoppingToken);
                await PerformCleanupAsync();
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

    /// <summary>Purges all finished games from persistence and memory immediately on service start.</summary>
    private async Task RunStartupCleanupAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var roomManager = scope.ServiceProvider.GetRequiredService<GameRoomManager>();
            await roomManager.VacuumStorageAsync(g => g.Status == GameStatus.Finished);
            _logger.LogInformation("Startup cleanup: all finished games purged");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup cleanup");
        }
    }

    private async Task PerformCleanupAsync()
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

            if (disconnectedPlayers.Count == 0) { continue; }

            bool stateChanged = false;
            bool gameDeleted = false;

            if (game.Status == GameStatus.Waiting)
            {
                // Signal deletion intent back out of the mutator — never call DeleteGame
                // from inside MutateGame since both acquire _lock and Lock is not reentrant.
                bool shouldDelete = false;

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
                            shouldDelete = true;
                            return;
                        }

                        if (g.HostId == player.Id)
                        {
                            g.HostId = g.Players[0].Id;
                            _logger.LogInformation("New host assigned in game {GameId}", g.GameId);
                        }
                    }
                });

                if (shouldDelete)
                {
                    roomManager.DeleteGame(game.GameId);
                    gameDeleted = true;
                    gamesCleanedUp++;
                    _logger.LogInformation(
                        "Game {GameId} deleted after all players disconnected", game.GameId);
                }
            }
            else if (game.Status == GameStatus.InProgress)
            {
                roomManager.MutateGame(game.GameId, (g, engine) =>
                {
                    if (engine == null) { return; }

                    foreach (var player in disconnectedPlayers)
                    {
                        if (player.IsBankrupt) { continue; }

                        var currentPlayer = g.GetCurrentPlayer();
                        bool wasCurrentPlayer = currentPlayer?.Id == player.Id;

                        engine.ResignPlayer(player.Id);
                        g.LogAction($"{player.Name} was bankrupted due to extended disconnection.");
                        playersRemoved++;
                        stateChanged = true;

                        _logger.LogInformation(
                            "Player {PlayerName} bankrupted in game {GameId} due to disconnect",
                            player.Name, g.GameId);

                        if (wasCurrentPlayer)
                        {
                            var activePlayers = g.Players.Where(p => !p.IsBankrupt).ToList();
                            if (activePlayers.Count > 0)
                            {
                                engine.NextTurn();
                                _logger.LogInformation(
                                    "Turn advanced in game {GameId} after current player disconnect", g.GameId);
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
                await BroadcastGameStateAsync(hubContext, game);
            }
        }

        if (gamesCleanedUp > 0 || playersRemoved > 0)
        {
            _logger.LogInformation(
                "Cleanup completed: {GamesRemoved} games removed, {PlayersAffected} players affected",
                gamesCleanedUp, playersRemoved);
        }
    }

    /// <summary>Broadcasts game state to all clients in a group.</summary>
    private static async Task BroadcastGameStateAsync(IHubContext<GameHub> hubContext, GameState game)
    {
        var dto = GameStateMapper.ToDto(game);
        await hubContext.Clients.Group(game.GameId).SendAsync("GameStateUpdated", dto);
    }
}