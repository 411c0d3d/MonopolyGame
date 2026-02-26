namespace MonopolyServer.Game.Models.Enums;

/// <summary>
/// Represents the current status of a Monopoly game.
/// </summary>
public enum GameStatus
{
    /// <summary>
    /// The game is waiting for players to join or for setup to complete.
    /// </summary>
    Waiting,

    /// <summary>
    /// The game is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// The game has finished.
    /// </summary>
    Finished,

    /// <summary>
    /// The game is temporarily paused.
    /// </summary>
    Paused
}