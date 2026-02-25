namespace MonopolyServer.DTOs;

/// <summary>
/// Serialized property data sent to clients.
/// </summary>
public class PropertyDto
{
    /// <summary>
    /// Unique property identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Display name of the property.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Property type as a string (e.g., "Street", "Utility", "Tax").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Board position index (0-39).
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Id of the owning player, or null if unowned.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// Display name of the owner, if any.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Number of houses currently on the property.
    /// </summary>
    public int HouseCount { get; set; }

    /// <summary>
    /// True when the property has a hotel.
    /// </summary>
    public bool HasHotel { get; set; }

    /// <summary>
    /// True when the property is mortgaged.
    /// </summary>
    public bool IsMortgaged { get; set; }

    /// <summary>
    /// Color group name for street properties, if applicable.
    /// </summary>
    public string? ColorGroup { get; set; }
}