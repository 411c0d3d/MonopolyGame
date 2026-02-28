using Microsoft.AspNetCore.SignalR;
using MonopolyServer.Bot;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Services;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Hubs;

/// <summary>
/// Admin hub for server management and game control operations.
/// Provides endpoints for force-ending games, kicking players, and pausing/resuming games.
/// All game-facing broadcasts go through the context of GameHub so lobby/game clients receive them.
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
        await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
        await _gameHubContext.Clients.Group(gameId)
            .SendAsync("PlayerKicked", new { playerId, playerName = player.Name });

        if (player.ConnectionId != null)
        {
            await _gameHubContext.Clients.Client(player.ConnectionId).SendAsync("Kicked", "You were removed by admin");
        }

        _logger.LogWarning("Admin kicked player {PlayerName} from game {GameId}", player.Name, gameId);
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
        await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
        await _gameHubContext.Clients.Group(gameId).SendAsync("GamePaused");

        _logger.LogInformation("Admin paused game {GameId}", gameId);
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
        await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));
        await _gameHubContext.Clients.Group(gameId).SendAsync("GameResumed");

        _logger.LogInformation("Admin resumed game {GameId}", gameId);
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
    /// Adds one or more bot players to a waiting or in-progress game.
    /// Bots are named "Bot 1", "Bot 2", etc., continuing from the highest existing bot number.
    /// </summary>
    public async Task AddBotToGame(string gameId, string adminKey, int count = 1)
    {
        if (!ValidateAdminKey(adminKey))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        if (count < 1 || count > GameConstants.MaxPlayers)
        {
            await Clients.Caller.SendAsync("Error", $"Bot count must be between 1 and {GameConstants.MaxPlayers}");
            return;
        }

        var game = _roomManager.GetGame(gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Game not found");
            return;
        }

        if (game.Status == GameStatus.Finished)
        {
            await Clients.Caller.SendAsync("Error", "Cannot add bots to a finished game");
            return;
        }

        int available = GameConstants.MaxPlayers - game.Players.Count;
        if (available <= 0)
        {
            await Clients.Caller.SendAsync("Error", "Game is full");
            return;
        }

        int toAdd = Math.Min(count, available);

        // Determine next bot number from existing bots to avoid name collisions
        int nextBotNumber = game.Players
            .Where(p => p.IsBot)
            .Select(p =>
            {
                var parts = p.Name.Split(' ');
                return parts.Length == 2 && int.TryParse(parts[1], out int n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max() + 1;

        var addedNames = new List<string>();

        for (int i = 0; i < toAdd; i++)
        {
            var botName = $"Bot {nextBotNumber++}";
            game.Players.Add(new Player(Guid.NewGuid().ToString(), botName)
            {
                IsBot = true,
                IsConnected = true,
                ConnectionId = null
            });
            game.LogAction($"{botName} joined the game as a bot.");
            addedNames.Add(botName);
        }

        await _roomManager.SaveGameAsync(gameId);

        // Broadcast through GameHub context so lobby clients receive the update
        await _gameHubContext.Clients.Group(gameId).SendAsync("GameStateUpdated", GameStateMapper.ToDto(game));

        _logger.LogInformation("Admin added {Count} bot(s) to game {GameId}: {Names}",
            toAdd, gameId, string.Join(", ", addedNames));

        // If game is already running, and it happens to be a bot's turn, kick it off
        if (game.Status == GameStatus.InProgress)
        {
            _botTurnOrchestrator.TryScheduleIfBotTurn(gameId, game);
        }
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