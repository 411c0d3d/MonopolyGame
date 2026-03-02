using MonopolyServer.Game.Models.Enums;
using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveLocks;
    private readonly ILogger<GameRoomManager> _logger;
    private readonly string _gamesDirectory;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    /// <summary>
    /// Initializes in-memory stores and loads existing games from disk.
    /// </summary>
    public GameRoomManager(ILogger<GameRoomManager> logger)
    {
        _logger = logger;
        _games = new Dictionary<string, GameState>();
        _engines = new Dictionary<string, GameEngine>();
        _saveLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _gamesDirectory = Path.Combine(AppContext.BaseDirectory,
            GameConstants.DataDirectoryName,
            GameConstants.GamesDirectoryName);

        InitializePersistence();
    }

    /// <summary>
    /// Creates the games directory if absent and loads all persisted game files into memory.
    /// </summary>
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
    /// Deserializes all game files from disk on startup.
    /// File reads run outside the lock; only the final dictionary population is synchronized.
    /// Corrupt files are deleted before populating memory.
    /// </summary>
    private void LoadGamesFromDisk()
    {
        string[] gameFiles;
        try
        {
            gameFiles = Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating game files from disk");
            return;
        }

        var loaded = new List<(GameState game, bool needsEngine)>();
        var corrupt = new List<string>();

        foreach (var filePath in gameFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);

                if (game == null)
                {
                    corrupt.Add(filePath);
                    continue;
                }

                loaded.Add((game, game.Status == GameStatus.InProgress));
            }
            catch (JsonException)
            {
                corrupt.Add(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading game file: {FileName}", Path.GetFileName(filePath));
            }
        }

        foreach (var path in corrupt)
        {
            try
            {
                File.Delete(path);
                _logger.LogWarning("Deleted corrupt game file: {FileName}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete corrupt file: {FileName}", Path.GetFileName(path));
            }
        }

        lock (_lock)
        {
            foreach (var (game, needsEngine) in loaded)
            {
                _games[game.GameId] = game;
                _saveLocks.TryAdd(game.GameId, new SemaphoreSlim(1, 1));

                if (needsEngine)
                {
                    _engines[game.GameId] = new GameEngine(game);
                }
            }
        }

        _logger.LogInformation("Loaded {Count} games from disk", loaded.Count);
    }

    /// <summary>
    /// Removes games matching the predicate from memory and deletes their disk files.
    /// Candidates are snapshotted under lock; all I/O runs outside to avoid blocking game operations.
    /// A secondary sweep removes orphaned disk files that were never tracked in memory.
    /// </summary>
    public void VacuumStorage(Func<GameState, bool> predicate)
    {
        List<(string gameId, string filePath)> candidates;

        lock (_lock)
        {
            candidates = _games.Values
                .Where(predicate)
                .Select(g => (g.GameId, Path.Combine(_gamesDirectory, $"{g.GameId}{GameConstants.GameFileExtension}")))
                .ToList();

            foreach (var (gameId, _) in candidates)
            {
                _games.Remove(gameId);
                _engines.Remove(gameId);
            }
        }

        foreach (var (gameId, filePath) in candidates)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                if (_saveLocks.TryRemove(gameId, out var sem))
                {
                    sem.Dispose();
                }

                _logger.LogInformation("Vacuumed game: {GameId}", gameId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting file for vacuumed game: {GameId}", gameId);
            }
        }

        try
        {
            var trackedIds = new HashSet<string>(candidates.Select(c => c.gameId));

            foreach (var filePath in Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}"))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);

                    if (game != null && predicate(game) && !trackedIds.Contains(game.GameId))
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Vacuumed orphaned game file: {FileName}", Path.GetFileName(filePath));
                    }
                }
                catch
                {
                    File.Delete(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during orphan file cleanup in vacuum");
        }
    }

    /// <summary>
    /// Persists a game to disk atomically using a temp-file-then-replace strategy.
    /// The JSON snapshot is taken under the global lock so the serialized state is always consistent.
    /// A per-game semaphore in _saveLocks serializes concurrent disk writes for the same game ID.
    /// </summary>
    public async Task SaveGameAsync(string gameId)
    {
        string? json;
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
            {
                return;
            }

            json = JsonSerializer.Serialize(game, JsonOptions);
        }

        var saveLock = _saveLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync();
        try
        {
            var filePath = Path.Combine(_gamesDirectory, $"{gameId}{GameConstants.GameFileExtension}");
            var tempPath = filePath + ".tmp";

            await File.WriteAllTextAsync(tempPath, json);
            File.Replace(tempPath, filePath, destinationBackupFileName: null);

            _logger.LogDebug("Saved game to disk: {GameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game {GameId} to disk", gameId);
        }
        finally
        {
            saveLock.Release();
        }
    }

    /// <summary>
    /// Deletes the JSON file for a game from disk.
    /// Called outside the global lock — I/O must never block in-memory operations.
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
    /// Creates a new game, registers it in memory, and fires a non-blocking background save.
    /// The game is immediately visible to all callers once the lock is released.
    /// </summary>
    public string CreateGame()
    {
        string gameId;
        lock (_lock)
        {
            gameId = GenerateGameId();
            var game = new GameState(gameId, null);
            _games[gameId] = game;
            _saveLocks.TryAdd(gameId, new SemaphoreSlim(1, 1));
        }

        _ = SaveGameAsync(gameId);

        _logger.LogInformation("Game created: {GameId}", gameId);
        return gameId;
    }

    /// <summary>
    /// Returns the live GameState reference for the given ID, or null if not found.
    /// Do not mutate the returned reference directly — use MutateGame instead.
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
    /// Returns a snapshot list of all live GameState references.
    /// The list itself is safe to iterate outside the lock; the objects inside are shared references.
    /// Do not mutate them — use MutateGame for any state changes.
    /// </summary>
    public List<GameState> GetAllGames()
    {
        lock (_lock)
        {
            return _games.Values.ToList();
        }
    }

    /// <summary>
    /// Returns a snapshot of all current game IDs, safe for iteration outside any lock.
    /// </summary>
    public List<string> GetAllGameIds()
    {
        lock (_lock)
        {
            return _games.Keys.ToList();
        }
    }

    /// <summary>
    /// Returns the GameEngine for an active game, or null if the game has not started or does not exist.
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
    /// Registers the GameEngine for a game once it transitions to InProgress.
    /// Replaces any existing engine entry for the same game ID.
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
    /// Finds the game a player currently belongs to by scanning all active games.
    /// Used primarily for disconnect cleanup when only the player ID is known.
    /// </summary>
    public GameState? GetGameByPlayerId(string playerId)
    {
        lock (_lock)
        {
            return _games.Values.FirstOrDefault(g => g.Players.Any(p => p.Id == playerId));
        }
    }

    /// <summary>
    /// Marks a player as disconnected without removing them from the game.
    /// The player may rejoin within the configured timeout window before cleanup evicts them.
    /// </summary>
    public void MarkPlayerDisconnected(string playerId)
    {
        lock (_lock)
        {
            var game = _games.Values.FirstOrDefault(g => g.Players.Any(p => p.Id == playerId));
            if (game == null) { return; }

            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) { return; }

            player.IsConnected = false;
            player.DisconnectedAt = DateTime.UtcNow;
            player.ConnectionId = null;
            _logger.LogInformation("Player {PlayerName} marked as disconnected in game {GameId}", player.Name, game.GameId);
        }
    }

    /// <summary>
    /// Restores a player's connection state after a successful reconnect.
    /// Returns false if the game or player cannot be found.
    /// </summary>
    public bool ReconnectPlayer(string gameId, string playerId, string newConnectionId)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game)) { return false; }

            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) { return false; }

            player.ConnectionId = newConnectionId;
            player.IsConnected = true;
            player.DisconnectedAt = null;

            _logger.LogInformation("Player {PlayerName} reconnected to game {GameId}", player.Name, gameId);
            return true;
        }
    }

    /// <summary>
    /// Removes a game from memory and disk.
    /// Dictionary removal is synchronised under the global lock.
    /// Disk I/O and semaphore disposal run outside the lock to avoid blocking other callers.
    /// </summary>
    public void DeleteGame(string gameId)
    {
        SemaphoreSlim? sem;

        lock (_lock)
        {
            if (!_games.Remove(gameId)) { return; }
            _engines.Remove(gameId);
            _saveLocks.TryRemove(gameId, out sem);
        }

        sem?.Dispose();
        DeleteGameFile(gameId);

        _logger.LogInformation("Game deleted: {GameId}", gameId);
    }

    /// <summary>
    /// Returns a snapshot of server health counters across all active games.
    /// All counts are read under the global lock for a consistent point-in-time view.
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
    /// Generates a unique game ID of configured length by retrying until no collision is found.
    /// Must be called inside the global lock since it reads _games.
    /// </summary>
    private string GenerateGameId()
    {
        string id;
        do
        {
            id = new string(Enumerable.Range(0, GameConstants.GameIdLength)
                .Select(_ => GameConstants.GameIdChars[Random.Shared.Next(GameConstants.GameIdChars.Length)])
                .ToArray());
        }
        while (_games.ContainsKey(id));

        return id;
    }

    /// <summary>
    /// Builds the shared JSON serializer options used for all GameState serialization.
    /// Configured once at startup and reused across all serialize/deserialize calls.
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

    /// <summary>
    /// Executes a mutator against a game's state and engine atomically under the global lock.
    /// This is the only correct way to modify game state — never mutate a reference from GetGame() directly.
    /// Returns false if the game does not exist.
    /// </summary>
    public bool MutateGame(string gameId, Action<GameState, GameEngine?> mutator)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
            {
                return false;
            }

            _engines.TryGetValue(gameId, out var engine);
            mutator(game, engine);
            return true;
        }
    }

    /// <summary>
    /// Executes an action atomically against both game state and engine under the global lock.
    /// Returns false if either the game or its engine is absent.
    /// Delegates to MutateGame to ensure a single consistent locking path across all mutations.
    /// </summary>
    public bool ExecuteWithEngine(string gameId, Action<GameState, GameEngine> action)
    {
        return MutateGame(gameId, (game, engine) =>
        {
            if (engine != null)
            {
                action(game, engine);
            }
        });
    }
}