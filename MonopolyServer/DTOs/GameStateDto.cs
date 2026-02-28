using MonopolyServer.Game.Models;

namespace MonopolyServer.DTOs;

/// <summary>
/// Serialized game state sent to clients via SignalR.
/// </summary>
public class GameStateDto
{
    /// <summary>
    /// Unique identifier for the game instance.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the host player, if any.
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>
    /// Display name of the host player, if any.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Current game status as a string (e.g., "Waiting", "InProgress", "Ended").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Current turn number.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Index of the current player in the Players list.
    /// </summary>
    public int CurrentPlayerIndex { get; set; }

    /// <summary>
    /// DTO for the current active player, if available.
    /// </summary>
    public PlayerDto? CurrentPlayer { get; set; }

    /// <summary>
    /// Players in turn order.
    /// </summary>
    public List<PlayerDto> Players { get; set; } = new();

    /// <summary>
    /// Board properties serialized for clients.
    /// </summary>
    public List<PropertyDto> Board { get; set; } = new();

    /// <summary>
    /// Chronological log of recent game events.
    /// </summary>
    public List<string> EventLog { get; set; } = new();

    /// <summary>
    /// UTC timestamp when the current turn started, used for turn timeouts.
    /// </summary>
    public DateTime? CurrentTurnStartedAt { get; set; }

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
    /// Timestamp when the game was finished, null if still active.
    /// </summary>
    public DateTime? FinishedAt { get; set; }

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
    /// Identifier of the winning player, if the game has ended with a winner.
    /// </summary>
    public string? WinnerId { get; set; }
}