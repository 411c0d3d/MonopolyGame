using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Game.Services;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Hubs;

/// <summary>
/// Admin hub for server management and game control operations.
/// Provides endpoints for force-ending games, kicking players, and pausing/resuming games.
/// </summary>
public class AdminHub : Hub
{
    private readonly GameRoomManager _roomManager;
    private readonly ILogger<AdminHub> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Constructor with dependency injection for game room management, logging, and configuration access.
    /// </summary>
    /// <param name="roomManager"></param>
    /// <param name="logger"></param>
    /// <param name="configuration"></param>
    public AdminHub(
        GameRoomManager roomManager,
        ILogger<AdminHub> logger,
        IConfiguration configuration)
    {
        _roomManager = roomManager;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Force end a game immediately. Marks game as finished without determining winner.
    /// </summary>
    public async Task ForceEndGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        game.Status = GameStatus.Finished;
        game.FinishedAt = DateTime.UtcNow;
        game.LogAction("Game was force-ended by admin.");

        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("GameForceEnded", new { gameId, reason = "Admin action" });

        _logger.LogWarning($"Admin force-ended game {gameId}");
    }

    /// <summary>
    /// Kick a player from a game. Removes player if game is waiting, marks bankrupt if in progress.
    /// </summary>
    public async Task KickPlayer(string gameId, string playerId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "Player not found");
            return;
        }

        if (game.Status == GameStatus.Waiting)
        {
            game.Players.Remove(player);
            game.LogAction($"{player.Name} was kicked by admin.");
        }
        else if (game.Status == GameStatus.InProgress)
        {
            player.IsBankrupt = true;
            player.IsConnected = false;
            game.LogAction($"{player.Name} was removed by admin (marked bankrupt).");

            var activePlayers = game.Players.Where(p => !p.IsBankrupt).ToList();
            if (activePlayers.Count <= 1)
            {
                game.Status = GameStatus.Finished;
                game.FinishedAt = DateTime.UtcNow;
                game.WinnerId = activePlayers.FirstOrDefault()?.Id;
            }
        }

        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("PlayerKicked", new { playerId, playerName = player.Name });

        if (player.ConnectionId != null)
        {
            await Clients.Client(player.ConnectionId).SendAsync("Kicked", "You were removed by admin");
        }

        _logger.LogWarning($"Admin kicked player {player.Name} from game {gameId}");
    }

    /// <summary>
    /// Pause a game in progress. Prevents all actions until resumed.
    /// </summary>
    public async Task PauseGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        if (game.Status != GameStatus.InProgress)
        {
            await Clients.Caller.SendAsync("Error", "Game is not in progress");
            return;
        }

        game.Status = GameStatus.Paused;
        game.LogAction("Game paused by admin.");

        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("GamePaused");

        _logger.LogInformation($"Admin paused game {gameId}");
    }

    /// <summary>
    /// Resume a paused game.
    /// </summary>
    public async Task ResumeGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        if (game.Status != GameStatus.Paused)
        {
            await Clients.Caller.SendAsync("Error", "Game is not paused");
            return;
        }

        game.Status = GameStatus.InProgress;
        game.LogAction("Game resumed by admin.");

        await _roomManager.SaveGameAsync(gameId);
        await Clients.Group(gameId).SendAsync("GameResumed");

        _logger.LogInformation($"Admin resumed game {gameId}");
    }

    /// <summary>
    /// Get detailed game state for debugging.
    /// </summary>
    public async Task GetGameDetails(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        var details = new
        {
            game.GameId,
            game.Status,
            game.Turn,
            game.CurrentPlayerIndex,
            PlayerCount = game.Players.Count,
            Players = game.Players.Select(p => new
            {
                p.Id,
                p.Name,
                p.Cash,
                p.Position,
                p.IsInJail,
                p.IsBankrupt,
                p.IsConnected,
                p.DisconnectedAt,
                PropertyCount = game.Board.GetPropertiesByOwner(p.Id).Count()
            }),
            Properties = game.Board.Spaces.Where(s => s.OwnerId != null).Select(s => new
            {
                s.Id,
                s.Name,
                s.OwnerId,
                s.HouseCount,
                s.HasHotel,
                s.IsMortgaged
            }),
            RecentLogs = game.EventLog.TakeLast(20)
        };

        await Clients.Caller.SendAsync("GameDetails", details);
    }

    /// <summary>
    /// Validate admin key from configuration.
    /// </summary>
    private bool ValidateAdminKey(string adminKey)
    {
        var configuredKey = _configuration["AdminKey"];
        return !string.IsNullOrEmpty(configuredKey) && configuredKey == adminKey;
    }
}