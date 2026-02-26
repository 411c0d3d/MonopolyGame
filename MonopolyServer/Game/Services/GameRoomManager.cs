using MonopolyServer.Game.Models.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Manages game room lifecycle and JSON file persistence.
/// Thread-safe singleton - all active games stored in memory and persisted to disk.
/// Each game = one JSON file: /data/games/{gameId}.json
/// </summary>
public class GameRoomManager
{
    private readonly Dictionary<string, GameState> _games;
    private readonly Dictionary<string, GameEngine> _engines;
    private readonly ILogger<GameRoomManager> _logger;
    private readonly string _gamesDirectory;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOptions = BuildJsonOptions();

    /// <summary>
    /// Constructor with dependency injection of logger. Initializes in-memory game store and loads existing games from disk.
    /// </summary>
    /// <param name="logger">
    /// ILogger for logging game room lifecycle events and errors. Injected via DI container.
    /// </param>
    public GameRoomManager(ILogger<GameRoomManager> logger)
    {
        _logger = logger;
        _games = new Dictionary<string, GameState>();
        _engines = new Dictionary<string, GameEngine>();
        _gamesDirectory = Path.Combine(AppContext.BaseDirectory, 
            GameConstants.DataDirectoryName, 
            GameConstants.GamesDirectoryName);

        InitializePersistence();
    }

    /// <summary>
    /// Initialize persistence - create directory and load existing games from disk.
    /// </summary>
    private void InitializePersistence()
    {
        try
        {
            if (!Directory.Exists(_gamesDirectory))
            {
                Directory.CreateDirectory(_gamesDirectory);
                _logger.LogInformation($"Created games directory: {_gamesDirectory}");
            }

            LoadGamesFromDisk();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing persistence");
        }
    }

    /// <summary>
    /// Load all game files from disk on startup.
    /// </summary>
    private void LoadGamesFromDisk()
    {
        try
        {
            var gameFiles = Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}");

            foreach (var filePath in gameFiles)
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var game = JsonSerializer.Deserialize<GameState>(json, _jsonOptions);

                    if (game != null)
                    {
                        _games[game.GameId] = game;

                        if (game.Status == GameStatus.InProgress)
                        {
                            var engine = new GameEngine(game);
                            _engines[game.GameId] = engine;
                        }

                        _logger.LogInformation($"Loaded game from disk: {game.GameId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error loading game file: {filePath}");
                }
            }

            _logger.LogInformation($"Loaded {_games.Count} games from disk");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading games from disk");
        }
    }

    /// <summary>
    /// Save a game to disk asynchronously. Snapshots state under lock before async I/O.
    /// </summary>
    public async Task SaveGameAsync(string gameId)
    {
        string? json;
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
                return;

            json = JsonSerializer.Serialize(game, _jsonOptions);
        }

        try
        {
            var filePath = Path.Combine(_gamesDirectory, $"{gameId}{GameConstants.GameFileExtension}");
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug($"Saved game to disk: {gameId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving game {gameId} to disk");
        }
    }

    /// <summary>
    /// Delete game file from disk.
    /// </summary>
    private void DeleteGameFile(string gameId)
    {
        try
        {
            var filePath = Path.Combine(_gamesDirectory, $"{gameId}{GameConstants.GameFileExtension}");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation($"Deleted game file: {gameId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting game file {gameId}");
        }
    }

    /// <summary>
    /// Create a new game and persist to disk.
    /// </summary>
    public string CreateGame()
    {
        lock (_lock)
        {
            var gameId = GenerateGameId();
            var game = new GameState(gameId, null);
            _games[gameId] = game;

            _ = SaveGameAsync(gameId);

            _logger.LogInformation($"Game created: {gameId}");
            return gameId;
        }
    }

    /// <summary>
    /// Get game by ID.
    /// </summary>
    public GameState? GetGame(string gameId)
    {
        lock (_lock)
        {
            _games.TryGetValue(gameId, out var game);
            return game;
        }
    }

    /// <summary>
    /// Get all games.
    /// </summary>
    public List<GameState> GetAllGames()
    {
        lock (_lock)
        {
            return _games.Values.ToList();
        }
    }

    /// <summary>
    /// Get game engine for active game.
    /// </summary>
    public GameEngine? GetGameEngine(string gameId)
    {
        lock (_lock)
        {
            _engines.TryGetValue(gameId, out var engine);
            return engine;
        }
    }

    /// <summary>
    /// Set game engine when game starts.
    /// </summary>
    public void SetGameEngine(string gameId, GameEngine engine)
    {
        lock (_lock)
        {
            _engines[gameId] = engine;
            _logger.LogInformation($"Engine set for game: {gameId}");
        }
    }

    /// <summary>
    /// Find game containing a specific player (for disconnect cleanup).
    /// </summary>
    public GameState? GetGameByPlayerId(string playerId)
    {
        lock (_lock)
        {
            return _games.Values.FirstOrDefault(g => g.Players.Any(p => p.Id == playerId));
        }
    }

    /// <summary>
    /// Mark a player as disconnected instead of removing them.
    /// Allows for rejoin within the timeout period.
    /// </summary>
    public void MarkPlayerDisconnected(string playerId)
    {
        lock (_lock)
        {
            var game = _games.Values.FirstOrDefault(g => g.Players.Any(p => p.Id == playerId));
            if (game != null)
            {
                var player = game.Players.FirstOrDefault(p => p.Id == playerId);
                if (player != null)
                {
                    player.IsConnected = false;
                    player.DisconnectedAt = DateTime.UtcNow;
                    player.ConnectionId = null; // Clear old connection ID
                    _logger.LogInformation($"Player {player.Name} marked as disconnected in game {game.GameId}");
                }
            }
        }
    }

    /// <summary>
    /// Reconnect a player to a game by updating their connection ID.
    /// </summary>
    public bool ReconnectPlayer(string gameId, string playerId, string newConnectionId)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
                return false;

            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
                return false;

            player.ConnectionId = newConnectionId;
            player.IsConnected = true;
            player.DisconnectedAt = null;
            
            _logger.LogInformation($"Player {player.Name} reconnected to game {gameId}");
            return true;
        }
    }

    /// <summary>
    /// Delete game from memory and disk.
    /// </summary>
    public void DeleteGame(string gameId)
    {
        lock (_lock)
        {
            _games.Remove(gameId);
            _engines.Remove(gameId);
            DeleteGameFile(gameId);
            _logger.LogInformation($"Game deleted: {gameId}");
        }
    }

    /// <summary>
    /// Get diagnostics/stats about active games.
    /// </summary>
    public ServerHealthStatsDto GetStats()
    {
        lock (_lock)
        {
            return new ServerHealthStatsDto(
                totalGames: _games.Count,
                gamesInProgress: _games.Values.Count(g => g.Status == GameStatus.InProgress),
                gamesWaiting: _games.Values.Count(g => g.Status == GameStatus.Waiting),
                gamesPaused: _games.Values.Count(g => g.Status == GameStatus.Paused),
                gamesFinished: _games.Values.Count(g => g.Status == GameStatus.Finished),
                totalPlayers: _games.Values.Sum(g => g.Players.Count)
            );

        }
    }

    /// <summary>
    /// Generate unique 8-character game ID.
    /// </summary>
    private string GenerateGameId()
    {
        var id = new string(Enumerable.Range(0, GameConstants.GameIdLength)
            .Select(_ => GameConstants.GameIdChars[Random.Shared.Next(GameConstants.GameIdChars.Length)])
            .ToArray());

        while (_games.ContainsKey(id))
        {
            id = new string(Enumerable.Range(0, GameConstants.GameIdLength)
                .Select(_ => GameConstants.GameIdChars[Random.Shared.Next(GameConstants.GameIdChars.Length)])
                .ToArray());
        }

        return id;
    }

    /// <summary>
    /// Build the shared JSON serializer options for GameState serialization.
    /// </summary>
    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}