namespace MonopolyServer.DTOs;

/// <summary>
/// Serialized player data sent to clients.
/// </summary>
public class PlayerDto
{
    /// <summary>
    /// Unique persistent player identifier (GUID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the player.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current cash balance.
    /// </summary>
    public int Cash { get; set; }

    /// <summary>
    /// Current board position index (0-39).
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// True if the player is currently in jail.
    /// </summary>
    public bool IsInJail { get; set; }

    /// <summary>
    /// True if the player has been declared bankrupt.
    /// </summary>
    public bool IsBankrupt { get; set; }

    /// <summary>
    /// Number of kept cards (e.g., Get Out of Jail Free) held by the player.
    /// </summary>
    public int KeptCardCount { get; set; }

    /// <summary>
    /// Whether the player is currently connected.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Timestamp when player disconnected.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }
}