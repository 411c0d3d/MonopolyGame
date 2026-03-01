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
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    /// <summary>
    /// Constructor with dependency injection of logger. Initializes in-memory game store and loads existing games from disk.
    /// </summary>
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

    /// <summary> Initialize persistence - create directory and load existing games from disk. </summary>
    private void InitializePersistence()
    {
        try
        {
            if (!Directory.Exists(_gamesDirectory))
            {
                Directory.CreateDirectory(_gamesDirectory);
                _logger.LogInformation("Created games directory: {Directory}", _gamesDirectory);
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
        lock (_lock)
        {
            try
            {
                var gameFiles = Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}");

                foreach (var filePath in gameFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);

                        if (game == null) continue;

                        _games[game.GameId] = game;
                        if (game.Status == GameStatus.InProgress)
                        {
                            _engines[game.GameId] = new GameEngine(game);
                        }
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Deleting corrupt game file: {FileName}", Path.GetFileName(filePath));
                        File.Delete(filePath);
                    }
                }

                _logger.LogInformation("Loaded {Count} games from disk", _games.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading games from disk");
            }
        }
    }

    /// <summary>
    /// Direct access to purge files from disk before or during service runtime.
    /// </summary>
    public void VacuumStorage(Func<GameState, bool> predicate)
    {
        lock (_lock)
        {
            var files = Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}");
            foreach (var path in files)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);
                    if (game != null && predicate(game))
                    {
                        File.Delete(path);
                        _games.Remove(game.GameId);
                        _engines.Remove(game.GameId);
                        _logger.LogInformation("Vacuumed game: {GameId}", game.GameId);
                    }
                }
                catch
                {
                    File.Delete(path); // Clear corrupt files
                }
            }
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

            json = JsonSerializer.Serialize(game, JsonOptions);
        }

        try
        {
            var filePath = Path.Combine(_gamesDirectory, $"{gameId}{GameConstants.GameFileExtension}");
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug("Saved game to disk: {GameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game {GameId} to disk", gameId);
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
                _logger.LogInformation("Deleted game file: {GameId}", gameId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting game file {GameId}", gameId);
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

            _logger.LogInformation("Game created: {GameId}", gameId);
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
            _logger.LogInformation("Engine set for game: {GameId}", gameId);
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
                    player.ConnectionId = null;
                    _logger.LogInformation("Player {PlayerName} marked as disconnected in game {GameId}", player.Name,
                        game.GameId);
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

            _logger.LogInformation("Player {PlayerName} reconnected to game {GameId}", player.Name, gameId);
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
            _logger.LogInformation("Game deleted: {GameId}", gameId);
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