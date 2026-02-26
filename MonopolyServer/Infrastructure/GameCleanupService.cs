using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Constants;
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
    /// Constructor with dependency injection of IServiceProvider for accessing scoped services like GameRoomManager and ILogger for logging cleanup actions.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="logger"></param>
    public GameCleanupService(
        IServiceProvider serviceProvider,
        ILogger<GameCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(GameConstants.CleanupIntervalMinutes), stoppingToken);
                await PerformCleanup();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during game cleanup");
            }
        }

        _logger.LogInformation("Game cleanup service stopped");
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
                _logger.LogInformation($"Cleaned up abandoned game: {game.GameId}");
                continue;
            }

            if (game.Status == GameStatus.Finished &&
                game.FinishedAt.HasValue &&
                now - game.FinishedAt.Value > TimeSpan.FromDays(GameConstants.FinishedGameCleanupDays))
            {
                roomManager.DeleteGame(game.GameId);
                gamesCleanedUp++;
                _logger.LogInformation($"Cleaned up finished game: {game.GameId}");
                continue;
            }

            var disconnectedPlayers = game.Players
                .Where(p => !p.IsConnected &&
                            p.DisconnectedAt.HasValue &&
                            now - p.DisconnectedAt.Value >
                            TimeSpan.FromMinutes(GameConstants.PlayerDisconnectTimeoutMinutes))
                .ToList();

            if (!disconnectedPlayers.Any())
            {
                continue;
            }

            bool stateChanged = false;

            if (game.Status == GameStatus.Waiting)
            {
                foreach (var player in disconnectedPlayers)
                {
                    game.Players.Remove(player);
                    game.LogAction($"{player.Name} was removed due to extended disconnection.");
                    playersRemoved++;
                    stateChanged = true;

                    if (game.Players.Count == 0)
                    {
                        roomManager.DeleteGame(game.GameId);
                        gamesCleanedUp++;
                        _logger.LogInformation($"Game {game.GameId} deleted after all players disconnected");
                        break;
                    }

                    if (game.HostId == player.Id && game.Players.Count > 0)
                    {
                        game.HostId = game.Players[0].Id;
                        _logger.LogInformation($"New host assigned in game {game.GameId}");
                    }
                }
            }
            else if (game.Status == GameStatus.InProgress)
            {
                var engine = roomManager.GetGameEngine(game.GameId);
                if (engine == null)
                {
                    continue;
                }

                foreach (var player in disconnectedPlayers)
                {
                    if (player.IsBankrupt)
                    {
                        continue;
                    }

                    var currentPlayer = game.GetCurrentPlayer();
                    bool wasCurrentPlayer = currentPlayer?.Id == player.Id;

                    engine.BankruptPlayer(player, creditor: null);
                    game.LogAction($"{player.Name} was bankrupted due to extended disconnection.");
                    playersRemoved++;
                    stateChanged = true;

                    _logger.LogInformation($"Player {player.Name} bankrupted in game {game.GameId} due to disconnect");

                    if (wasCurrentPlayer)
                    {
                        var activePlayers = game.Players.Where(p => !p.IsBankrupt).ToList();
                        if (activePlayers.Count > 0)
                        {
                            engine.NextTurn();
                            _logger.LogInformation($"Turn advanced in game {game.GameId} after current player disconnect");
                        }
                    }

                    if (game.HostId == player.Id && game.Players.Count > 0)
                    {
                        var activePlayer = game.Players.FirstOrDefault(p => !p.IsBankrupt);
                        if (activePlayer != null)
                        {
                            game.HostId = activePlayer.Id;
                            _logger.LogInformation($"New host assigned in game {game.GameId}");
                        }
                    }
                }
            }

            if (stateChanged)
            {
                await roomManager.SaveGameAsync(game.GameId);
                await BroadcastGameState(hubContext, game);
            }
        }

        if (gamesCleanedUp > 0 || playersRemoved > 0)
        {
            _logger.LogInformation(
                $"Cleanup completed: {gamesCleanedUp} games removed, {playersRemoved} players affected");
        }
    }

    private async Task BroadcastGameState(IHubContext<GameHub> hubContext, MonopolyServer.Game.Models.GameState game)
    {
        var dto = new
        {
            GameId = game.GameId,
            Status = game.Status.ToString(),
            Turn = game.Turn,
            CurrentPlayerIndex = game.CurrentPlayerIndex,
            Players = game.Players.Select(p => new
            {
                Id = p.Id,
                Name = p.Name,
                Cash = p.Cash,
                Position = p.Position,
                IsInJail = p.IsInJail,
                IsBankrupt = p.IsBankrupt,
                IsConnected = p.IsConnected,
                DisconnectedAt = p.DisconnectedAt
            }).ToList(),
            EventLog = game.EventLog.TakeLast(10).ToList()
        };

        await hubContext.Clients.Group(game.GameId).SendAsync("GameStateUpdated", dto);
    }
}