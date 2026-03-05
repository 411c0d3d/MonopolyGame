using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Bot;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Hubs;

/// <summary>
/// Admin hub for server management and game control operations.
/// All game state mutations go through GameRoomManager.MutateGame to run under the global lock.
/// Persistence and broadcasts always happen outside the lock.
/// </summary>
public class AdminHub : Hub
{
    private readonly GameRoomManager _roomManager;
    private readonly IConfiguration _configuration;
    private readonly BotTurnOrchestrator _botTurnOrchestrator;
    private readonly IHubContext<GameHub> _gameHubContext;
    private readonly ILogger<AdminHub> _logger;

    /// <summary>
    /// Constructor with dependency injection for game room management, logging, and configuration access.
    /// </summary>
    /// <param name="roomManager">Manages game rooms, including retrieval, updates, and persistence of game state.</param>
    /// <param name="botTurnOrchestrator">Orchestrates and schedules bot turns for automated players in active games.</param>
    /// <param name="logger">Logger used to record hub operations, warnings, and admin actions.</param>
    /// <param name="configuration">Provides access to application configuration values (e.g., the admin key).</param>
    /// <param name="gameHubContext">The Game Hub Context.</param>
    public AdminHub(
        GameRoomManager roomManager,
        BotTurnOrchestrator botTurnOrchestrator,
        IHubContext<GameHub> gameHubContext,
        ILogger<AdminHub> logger,
        IConfiguration configuration)
    {
        _roomManager = roomManager;
        _botTurnOrchestrator = botTurnOrchestrator;
        _gameHubContext = gameHubContext;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Force end a game immediately. Marks game as finished without determining a winner.
    /// </summary>
    public async Task ForceEndGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(ForceEndGame), gameId, "Unauthorized");
            return;
        }

        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(ForceEndGame), gameId, "Game not found");
            return;
        }

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            g.Status = GameStatus.Finished;
            g.FinishedAt = DateTime.UtcNow;
            g.LogAction("Game was force-ended by admin.");
        });

        if (!found)
        {
            await SendError(nameof(ForceEndGame), gameId, "Game vanished before force-end could complete");
            return;
        }

        await _roomManager.SaveGameAsync(gameId);
        await _gameHubContext.Clients.Group(gameId)
            .SendAsync("GameForceEnded", new { gameId, reason = "Admin action" });

        _logger.LogWarning("Admin force-ended game {GameId}", gameId);
    }

    /// <summary>
    /// Kick a player from a game. Removes player if waiting, marks bankrupt if in progress.
    /// </summary>
    public async Task KickPlayer(string gameId, string playerId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(KickPlayer), gameId, "Unauthorized");
            return;
        }

        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(KickPlayer), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;
        string? playerName = null;
        string? connectionId = null;

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            var player = g.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                errorMsg = "Player not found";
                return;
            }

            playerName = player.Name;
            connectionId = player.ConnectionId;

            if (g.Status == GameStatus.Waiting)
            {
                g.Players.Remove(player);
                g.LogAction($"{player.Name} was kicked by admin.");
            }
            else if (g.Status == GameStatus.InProgress)
            {
                player.IsBankrupt = true;
                player.IsConnected = false;
                g.LogAction($"{player.Name} was removed by admin (marked bankrupt).");

                var activePlayers = g.Players.Where(p => !p.IsBankrupt).ToList();
                if (activePlayers.Count <= 1)
                {
                    g.Status = GameStatus.Finished;
                    g.FinishedAt = DateTime.UtcNow;
                    g.WinnerId = activePlayers.FirstOrDefault()?.Id;
                }
            }
        });

        if (!found || errorMsg != null)
        {
            var reason = !found ? "Game vanished before kick could complete" : errorMsg!;
            await SendError(nameof(KickPlayer), gameId, reason);
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game != null)
        {
            await _roomManager.SaveGameAsync(gameId);
            await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
            await _gameHubContext.Clients.Group(gameId).SendAsync("PlayerKicked", new { playerId, playerName });
        }

        if (connectionId != null)
        {
            await _gameHubContext.Clients.Client(connectionId).SendAsync("Kicked", "You were removed by admin");
        }

        _logger.LogWarning("Admin kicked player {PlayerName} from game {GameId}", playerName, gameId);
    }

    /// <summary>
    /// Pause a game in progress. Prevents all actions until resumed.
    /// </summary>
    public async Task PauseGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(PauseGame), gameId, "Unauthorized");
            return;
        }

        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(PauseGame), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            if (g.Status != GameStatus.InProgress)
            {
                errorMsg = "Game is not in progress";
                return;
            }

            g.Status = GameStatus.Paused;
            g.LogAction("Game paused by admin.");
        });

        if (!found)
        {
            await SendError(nameof(PauseGame), gameId, "Game vanished before pause could complete");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(PauseGame), gameId, errorMsg);
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game != null)
        {
            await _roomManager.SaveGameAsync(gameId);
            await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
            await _gameHubContext.Clients.Group(gameId).SendAsync("GamePaused");
        }

        _logger.LogInformation("Admin paused game {GameId}", gameId);
    }

    /// <summary>
    /// Resume a paused game.
    /// </summary>
    public async Task ResumeGame(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(ResumeGame), gameId, "Unauthorized");
            return;
        }

        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(ResumeGame), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            if (g.Status != GameStatus.Paused)
            {
                errorMsg = "Game is not paused";
                return;
            }

            g.Status = GameStatus.InProgress;
            g.LogAction("Game resumed by admin.");
        });

        if (!found)
        {
            await SendError(nameof(ResumeGame), gameId, "Game vanished before resume could complete");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(ResumeGame), gameId, errorMsg);
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game != null)
        {
            await _roomManager.SaveGameAsync(gameId);
            await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
            await _gameHubContext.Clients.Group(gameId).SendAsync("GameResumed");
        }

        _logger.LogInformation("Admin resumed game {GameId}", gameId);
    }

    /// <summary>
    /// Get detailed game state for debugging.
    /// </summary>
    public async Task GetGameDetails(string gameId, string adminKey)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(GetGameDetails), gameId, "Unauthorized");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await SendError(nameof(GetGameDetails), gameId, "Game not found");
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
    /// Adds one or more bot players to a waiting or in-progress game.
    /// Bots are named "Bot N", continuing from the highest existing bot number.
    /// </summary>
    public async Task AddBotToGame(string gameId, string adminKey, int count = 1)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await SendError(nameof(AddBotToGame), gameId, "Unauthorized");
            return;
        }

        if (count < 1 || count > GameConstants.MaxPlayers)
        {
            await SendError(nameof(AddBotToGame), gameId,
                $"Bot count must be between 1 and {GameConstants.MaxPlayers}");
            return;
        }

        if (_roomManager.GetGame(gameId) == null)
        {
            await SendError(nameof(AddBotToGame), gameId, "Game not found");
            return;
        }

        string? errorMsg = null;
        var addedNames = new List<string>();
        bool isInProgress = false;

        var found = _roomManager.MutateGame(gameId, (g, _) =>
        {
            if (g.Status == GameStatus.Finished)
            {
                errorMsg = "Cannot add bots to a finished game";
                return;
            }

            int available = GameConstants.MaxPlayers - g.Players.Count;
            if (available <= 0)
            {
                errorMsg = "Game is full";
                return;
            }

            int toAdd = Math.Min(count, available);
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
                addedNames.Add(botName);
            }

            isInProgress = g.Status == GameStatus.InProgress;
        });

        if (!found)
        {
            await SendError(nameof(AddBotToGame), gameId, "Game vanished before bots could be added");
            return;
        }

        if (errorMsg != null)
        {
            await SendError(nameof(AddBotToGame), gameId, errorMsg);
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game != null)
        {
            await _roomManager.SaveGameAsync(gameId);
            await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));

            _logger.LogInformation("Admin added {Count} bot(s) to game {GameId}: {Names}",
                addedNames.Count, gameId, string.Join(", ", addedNames));

            if (isInProgress)
            {
                _botTurnOrchestrator.TryScheduleIfBotTurn(gameId, game);
            }
        }
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
    /// Validates the admin key against configuration.
    /// </summary>
    private bool ValidateAdminKey(string adminKey)
    {
        var configuredKey = _configuration["AdminKey"];
        return !string.IsNullOrEmpty(configuredKey) && configuredKey == adminKey;
    }
}