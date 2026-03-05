using MonopolyServer.Data.Repositories;
using MonopolyServer.DTOs;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Manages game room lifecycle. Thread-safe singleton — all active games cached in memory,
/// persisted via IGameRepository. Call InitializeAsync() at startup before serving requests.
/// </summary>
public class GameRoomManager
{
    private readonly Dictionary<string, GameState> _games;
    private readonly Dictionary<string, GameEngine> _engines;
    private readonly IGameRepository _repository;
    private readonly ILogger<GameRoomManager> _logger;
    private readonly Lock _lock = new();

    /// <summary>Initializes in-memory stores. Call InitializeAsync() before use.</summary>
    public GameRoomManager(IGameRepository repository, ILogger<GameRoomManager> logger)
    {
        _repository = repository;
        _logger = logger;
        _games = new Dictionary<string, GameState>();
        _engines = new Dictionary<string, GameEngine>();
    }

    /// <summary>
    /// Loads all persisted games from the repository into memory.
    /// In-progress games get a GameEngine instance. Must be awaited at startup before serving requests.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var games = await _repository.GetAllAsync();

            lock (_lock)
            {
                foreach (var game in games)
                {
                    _games[game.GameId] = game;

                    if (game.Status == GameStatus.InProgress)
                    {
                        _engines[game.GameId] = new GameEngine(game);
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} games from repository", games.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading games from repository during initialization");
        }
    }

    /// <summary>
    /// Persists the current in-memory state of a game to the repository.
    /// A consistent snapshot is read under the lock; the async upsert runs outside it.
    /// </summary>
    public async Task SaveGameAsync(string gameId)
    {
        GameState? snapshot;

        lock (_lock)
        {
            _games.TryGetValue(gameId, out snapshot);
        }

        if (snapshot == null)
        {
            return;
        }

        await _repository.UpsertAsync(snapshot);
    }

    /// <summary>
    /// Removes games matching the predicate from memory and deletes them from the repository.
    /// Candidate IDs are snapshotted under the lock; deletes run concurrently outside it.
    /// </summary>
    public async Task VacuumStorageAsync(Func<GameState, bool> predicate)
    {
        List<string> candidates;

        lock (_lock)
        {
            candidates = _games.Values
                .Where(predicate)
                .Select(g => g.GameId)
                .ToList();

            foreach (var id in candidates)
            {
                _games.Remove(id);
                _engines.Remove(id);
            }
        }

        await Task.WhenAll(candidates.Select(id => _repository.DeleteAsync(id)));

        _logger.LogInformation("Vacuumed {Count} games", candidates.Count);
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
            _games[gameId] = new GameState(gameId, null);
        }

        _ = SaveGameAsync(gameId);

        _logger.LogInformation("Game created: {GameId}", gameId);
        return gameId;
    }

    /// <summary>
    /// Returns the live GameState reference for the given ID, or null if not found.
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
    /// </summary>
    public List<GameState> GetAllGames()
    {
        lock (_lock)
        {
            return _games.Values.ToList();
        }
    }

    /// <summary>
    /// Returns a snapshot of all current game IDs.
    /// </summary>
    public List<string> GetAllGameIds()
    {
        lock (_lock)
        {
            return _games.Keys.ToList();
        }
    }

    /// <summary>
    /// Returns the GameEngine for an active game, or null if not started or not found.
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
    /// Registers or replaces the GameEngine for a game once it transitions to InProgress.
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
    /// Finds the game a player belongs to by scanning all active games.
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
            if (game == null)
            {
                return;
            }

            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                return;
            }

            player.IsConnected = false;
            player.DisconnectedAt = DateTime.UtcNow;
            player.ConnectionId = null;

            _logger.LogInformation(
                "Player {PlayerName} marked as disconnected in game {GameId}", player.Name, game.GameId);
        }
    }

    /// <summary>Restores a player's connection state after reconnect. Returns false if game or player not found.</summary>
    public bool ReconnectPlayer(string gameId, string playerId, string newConnectionId)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
            {
                return false;
            }

            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                return false;
            }

            player.ConnectionId = newConnectionId;
            player.IsConnected = true;
            player.DisconnectedAt = null;

            _logger.LogInformation(
                "Player {PlayerName} reconnected to game {GameId}", player.Name, gameId);
            return true;
        }
    }

    /// <summary>
    /// Removes a game from memory and fires a non-blocking background delete from the repository.
    /// Dictionary removal is synchronised under the lock; the async delete runs outside it.
    /// </summary>
    public void DeleteGame(string gameId)
    {
        lock (_lock)
        {
            if (!_games.Remove(gameId))
            {
                return;
            }

            _engines.Remove(gameId);
        }

        _ = _repository.DeleteAsync(gameId);

        _logger.LogInformation("Game deleted: {GameId}", gameId);
    }

    /// <summary>
    /// Returns a consistent point-in-time snapshot of server health counters.
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
    /// Executes a mutator against a game's state and engine atomically under the global lock.
    /// This is the only correct way to modify game state — never mutate a GetGame() reference directly.
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

    /// <summary>
    /// Creates a GameEngine for the game if one does not already exist, then executes the mutator.
    /// Used exclusively for game start — ensures engine creation and first mutation are atomic.
    /// Returns false if the game does not exist.
    /// </summary>
    public bool InitializeEngine(string gameId, Action<GameState, GameEngine> mutator)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameId, out var game))
            {
                return false;
            }

            if (!_engines.TryGetValue(gameId, out var engine))
            {
                engine = new GameEngine(game);
                _engines[gameId] = engine;
            }

            mutator(game, engine);
            return true;
        }
    }

    /// <summary>
    /// Generates a unique game ID. Must be called inside the global lock.
    /// </summary>
    private string GenerateGameId()
    {
        string id;
        do
        {
            id = new string(Enumerable.Range(0, GameConstants.GameIdLength)
                .Select(_ => GameConstants.GameIdChars[Random.Shared.Next(GameConstants.GameIdChars.Length)])
                .ToArray());
        } while (_games.ContainsKey(id));

        return id;
    }
}