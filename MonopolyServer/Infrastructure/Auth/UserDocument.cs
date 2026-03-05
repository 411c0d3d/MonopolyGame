using System.Text.Json.Serialization;

namespace MonopolyServer.Infrastructure.Auth;

/// <summary>
/// Cosmos document representing an authenticated user.
/// Id is the B2C objectId — the persistent identity across sessions.
/// Partition key is /id.
/// </summary>
public sealed class UserDocument
{
    /// <summary>
    /// Unique identifier for the user, corresponding to the B2C objectId.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User's email address, used as the primary login identifier.
    /// </summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the user, shown in the UI.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user has administrative privileges.
    /// </summary>
    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    /// <summary>
    /// UTC timestamp when the user document was created, corresponding to the first login time.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp of the user's last login, updated on each successful authentication.
    /// </summary>
    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
}