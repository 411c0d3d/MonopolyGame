using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

/// <summary>
/// Pure data model representing the current state of a Monopoly game.
/// Contains no business logic—that belongs in GameEngine.
/// </summary>
public class GameState
{
    /// <summary>
    /// Unique identifier for the game instance.
    /// </summary>
    public string GameId { get; set; }

    /// <summary>
    /// Identifier of the host player, if any.
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>
    /// Current status of the game (Waiting, InProgress, Ended, etc.).
    /// </summary>
    public GameStatus Status { get; set; }

    /// <summary>
    /// The game board containing all properties and special spaces.
    /// </summary>
    public Board Board { get; set; }

    /// <summary>
    /// Players participating in the game in turn order.
    /// </summary>
    public List<Player> Players { get; set; }

    /// <summary>
    /// Index into Players for the current active player.
    /// </summary>
    public int CurrentPlayerIndex { get; set; }

    /// <summary>
    /// UTC timestamp when the game was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the game was started, if started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the game ended, if ended.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Current turn counter.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Chronological log of game events for audit and replay.
    /// </summary>
    public List<string> GameLog { get; set; }

    // Transient game state
    /// <summary>
    /// Last dice roll value recorded in the game.
    /// </summary>
    public int LastDiceRoll { get; set; }

    /// <summary>
    /// True if the last roll was a double.
    /// </summary>
    public bool DoubleRolled { get; set; }
    
    /// <summary>
    /// List of pending trade offers between players.
    /// </summary>
    public List<TradeOffer> PendingTrades { get; set; } = new();

    /// <summary>
    /// Initialize a new GameState for the given game and host.
    /// </summary>
    public GameState(string gameId, string hostId)
    {
        GameId = gameId;
        HostId = hostId;
        Status = GameStatus.Waiting;
        Board = new Board();
        Players = new List<Player>();
        CurrentPlayerIndex = 0;
        CreatedAt = DateTime.UtcNow;
        Turn = 0;
        GameLog = new List<string>();
        LastDiceRoll = 0;
        DoubleRolled = false;
    }

    /// <summary>
    /// Simple getter—no logic.
    /// </summary>
    public Player? GetCurrentPlayer()
    {
        if (Players.Count == 0 || CurrentPlayerIndex >= Players.Count)
            return null;

        return Players[CurrentPlayerIndex];
    }

    /// <summary>
    /// Simple getter—no logic.
    /// </summary>
    public Player? GetPlayerById(string playerId)
    {
        return Players.FirstOrDefault(p => p.Id == playerId);
    }

    /// <summary>
    /// Log an action. Used by GameEngine to record events.
    /// </summary>
    public void LogAction(string message)
    {
        GameLog.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
    }
}