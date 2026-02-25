using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Engine;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Service that manages the lifecycle of game rooms and their associated GameEngine instances.
/// Handles creation, retrieval, and cleanup of games.
/// Thread-safe for concurrent game sessions.
/// </summary>
public class GameRoomManager
{
    private readonly Dictionary<string, GameState> _games = new();
    private readonly Dictionary<string, GameEngine> _engines = new();
    private readonly object _lock = new object();

    /// <summary>
    /// Create a new game room with a unique ID.
    /// </summary>
    public string CreateGame()
    {
        lock (_lock)
        {
            string gameId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            // Ensure unique ID (highly unlikely but be safe)
            while (_games.ContainsKey(gameId))
            {
                gameId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            }

            var gameState = new GameState(gameId, null);
            _games[gameId] = gameState;

            return gameId;
        }
    }

    /// <summary>
    /// Get a game by its ID.
    /// </summary>
    public GameState? GetGame(string gameId)
    {
        lock (_lock)
        {
            return _games.TryGetValue(gameId, out var game) ? game : null;
        }
    }

    /// <summary>
    /// Get the GameEngine for a game (or null if not started).
    /// </summary>
    public GameEngine? GetGameEngine(string gameId)
    {
        lock (_lock)
        {
            return _engines.TryGetValue(gameId, out var engine) ? engine : null;
        }
    }

    /// <summary>
    /// Set the GameEngine for a game (called when game starts).
    /// </summary>
    public void SetGameEngine(string gameId, GameEngine engine)
    {
        lock (_lock)
        {
            if (_games.ContainsKey(gameId))
            {
                _engines[gameId] = engine;
            }
        }
    }

    /// <summary>
    /// Find the game that a player is in by their connection ID.
    /// Used for cleanup on disconnect.
    /// </summary>
    public GameState? GetGameByPlayerId(string playerId)
    {
        lock (_lock)
        {
            return _games.Values.FirstOrDefault(g => g.Players.Any(p => p.Id == playerId));
        }
    }

    /// <summary>
    /// Delete a game room (called when no players remain).
    /// </summary>
    public void DeleteGame(string gameId)
    {
        lock (_lock)
        {
            _games.Remove(gameId);
            _engines.Remove(gameId);
        }
    }

    /// <summary>
    /// Get all active games (for lobby/status display).
    /// </summary>
    public List<GameState> GetAllGames()
    {
        lock (_lock)
        {
            return _games.Values.ToList();
        }
    }

    /// <summary>
    /// Get game statistics (for diagnostics).
    /// </summary>
    public object GetStats()
    {
        lock (_lock)
        {
            return new
            {
                totalGames = _games.Count,
                gamesInProgress = _games.Values.Count(g => g.Status == GameStatus.InProgress),
                gamesWaiting = _games.Values.Count(g => g.Status == GameStatus.Waiting),
                totalPlayers = _games.Values.Sum(g => g.Players.Count)
            };
        }
    }
}