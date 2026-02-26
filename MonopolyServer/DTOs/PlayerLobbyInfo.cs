namespace MonopolyServer.DTOs;

/// <summary>
/// Player info visible in lobby/room.
/// </summary>
public class PlayerLobbyInfo
{
    /// <summary>
    /// Unique player identifier (usually connection id or user id).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in the lobby.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True when the player is the host of the lobby/room.
    /// </summary>
    public bool IsHost { get; set; }

    /// <summary>
    /// Timestamp indicating when the player joined the lobby.
    /// </summary>
    public DateTime JoinedAt { get; set; }
}