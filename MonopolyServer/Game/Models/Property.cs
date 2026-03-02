using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

/// <summary>
/// Represents a property on the Monopoly board (street, railroad, or utility).
/// </summary>
public class Property
{
    /// <summary>
    /// Unique identifier for the property.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Display name of the property.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Type of the property (Street, Railroad, Utility).
    /// </summary>
    public PropertyType Type { get; set; }

    /// <summary>
    /// Position on the board (0-39).
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Player id of the owner, or null if unowned.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// Whether the property is mortgaged.
    /// </summary>
    public bool IsMortgaged { get; set; }

    /// <summary>
    /// Number of houses on the property (0-4). Use <see cref="HasHotel"/> for a hotel.
    /// </summary>
    public int HouseCount { get; set; }

    /// <summary>
    /// Whether the property has a hotel.
    /// </summary>
    public bool HasHotel { get; set; }

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

    /// <summary>
    /// Color group name for streets, or null for non-street properties.
    /// </summary>
    public string? ColorGroup { get; set; }

    /// <summary>
    /// Constructor for street properties.
    /// </summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="name">Property name.</param>
    /// <param name="position">Board position (0-39).</param>
    /// <param name="purchasePrice">Purchase price.</param>
    /// <param name="mortgageValue">Mortgage value.</param>
    /// <param name="houseCost">Cost per house.</param>
    /// <param name="hotelCost">Cost for a hotel.</param>
    /// <param name="rentValues">Rent values array.</param>
    /// <param name="colorGroup">Color group name.</param>
    public Property(int id, string name, int position, int purchasePrice, int mortgageValue,
        int houseCost, int hotelCost, int[] rentValues, string colorGroup)
    {
        Id = id;
        Name = name;
        Type = PropertyType.Street;
        Position = position;
        PurchasePrice = purchasePrice;
        MortgageValue = mortgageValue;
        HouseCost = houseCost;
        HotelCost = hotelCost;
        RentValues = rentValues;
        ColorGroup = colorGroup;
        OwnerId = null;
        IsMortgaged = false;
        HouseCount = 0;
        HasHotel = false;
    }

    /// <summary>
    /// Constructor for railroad properties.
    /// </summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="name">Property name.</param>
    /// <param name="position">Board position (0-39).</param>
    /// <param name="purchasePrice">Purchase price.</param>
    /// <param name="mortgageValue">Mortgage value.</param>
    public Property(int id, string name, int position, int purchasePrice, int mortgageValue)
    {
        Id = id;
        Name = name;
        Type = PropertyType.Railroad;
        Position = position;
        PurchasePrice = purchasePrice;
        MortgageValue = mortgageValue;
        OwnerId = null;
        IsMortgaged = false;
    }

    /// <summary>
    /// Constructor for utilities (and fallback for other simple types).
    /// </summary>
    /// <param name="id">Property identifier.</param>
    /// <param name="name">Property name.</param>
    /// <param name="position">Board position (0-39).</param>
    /// <param name="propertyType">Type of property; expected <see cref="PropertyType.Utility"/> for utilities.</param>
    public Property(int id, string name, int position, PropertyType propertyType)
    {
        Id = id;
        Name = name;
        Type = propertyType;
        Position = position;
        PurchasePrice = propertyType switch
        {
            PropertyType.Utility => 150,
            PropertyType.Tax when Name.Contains("income", StringComparison.OrdinalIgnoreCase) => 200,
            PropertyType.Tax => 100,
            _ => 0
        };
        MortgageValue = propertyType == PropertyType.Utility ? 75 : 0;
        OwnerId = null;
        IsMortgaged = false;
    }
}