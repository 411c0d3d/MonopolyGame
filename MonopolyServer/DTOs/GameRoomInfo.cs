namespace MonopolyServer.DTOs;

/// <summary>
/// Room/lobby information sent to clients for game discovery.
/// </summary>
public class GameRoomInfo
{
    /// <summary>
    /// Unique identifier for the game room.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Connection identifier of the host player.
    /// </summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the host player.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Current number of players in the room.
    /// </summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// Maximum allowed players for the room.
    /// </summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>
    /// List of players currently in the lobby.
    /// </summary>
    public List<PlayerLobbyInfo> Players { get; set; } = new();

    /// <summary>
    /// Time when the room was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Current status of the room (for example: Open, InGame, Closed).
    /// </summary>
    public string Status { get; set; } = string.Empty;
}