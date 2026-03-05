using MonopolyServer.Game.Models;

namespace MonopolyServer.Data.Repositories;

/// <summary>
/// Persistence contract for GameState documents.
/// </summary>
public interface IGameRepository
{
    /// <summary>
    /// Returns the game with the given ID, or null if not found or corrupt.
    /// </summary>
    Task<GameState?> GetAsync(string gameId);

    /// <summary>
    /// Returns all persisted games. Corrupt documents are deleted and excluded.
    /// </summary>
    Task<IReadOnlyList<GameState>> GetAllAsync();

    /// <summary>
    /// Creates or replaces the persisted game document.
    /// </summary>
    Task UpsertAsync(GameState game);

    /// <summary>
    /// Deletes the game document. No-ops if the document does not exist.
    /// </summary>
    Task DeleteAsync(string gameId);
}