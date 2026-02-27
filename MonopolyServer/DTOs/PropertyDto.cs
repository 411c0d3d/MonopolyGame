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

    /// <summary>
    /// Purchase price of the property.
    /// </summary>
    public int PurchasePrice { get; set; }

    /// <summary>
    /// Mortgage value of the property.
    /// </summary>
    public int MortgageValue { get; set; }

    /// <summary>
    /// Cost to buy a house on this property.
    /// </summary>
    public int HouseCost { get; set; }

    /// <summary>
    /// Cost to buy a hotel on this property.
    /// </summary>
    public int HotelCost { get; set; }

    /// <summary>
    /// Rent values indexed by development: [base, 1house, 2house, 3house, 4house, hotel].
    /// </summary>
    public int[] RentValues { get; set; } = Array.Empty<int>();
}