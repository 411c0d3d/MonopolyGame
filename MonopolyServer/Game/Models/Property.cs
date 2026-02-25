using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

public class Property
{
    public int Id { get; set; }
    public string Name { get; set; }
    public PropertyType Type { get; set; }
    public int Position { get; set; } // 0-39 on board

    // Ownership & development
    public string? OwnerId { get; set; }
    public bool IsMortgaged { get; set; }
    public int HouseCount { get; set; } // 0-4, 5 = hotel
    public bool HasHotel { get; set; }

    // Financial info
    public int PurchasePrice { get; set; }
    public int MortgageValue { get; set; }
    public int HouseCost { get; set; }
    public int HotelCost { get; set; }
    public int[] RentValues { get; set; } = Array.Empty<int>(); // [base, 1house, 2house, 3house, 4house, hotel]

    // Color group (for streets only)
    public string? ColorGroup { get; set; }

    // Constructor for streets
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

    // Constructor for railroads
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

    // Constructor for utilities
    public Property(int id, string name, int position, PropertyType propertyType)
    {
        Id = id;
        Name = name;
        Type = propertyType;
        Position = position;
        PurchasePrice = propertyType == PropertyType.Utility ? 150 : 0;
        MortgageValue = propertyType == PropertyType.Utility ? 75 : 0;
        OwnerId = null;
        IsMortgaged = false;
    }

    public int GetCurrentRent(int diceRoll = 0, int numRailroadsOwned = 1, int numUtilitiesOwned = 1)
    {
        if (IsMortgaged || OwnerId == null)
            return 0;

        return Type switch
        {
            PropertyType.Street => GetStreetRent(),
            PropertyType.Railroad => 25 * numRailroadsOwned,
            PropertyType.Utility => diceRoll * (numUtilitiesOwned == 1 ? 4 : 10),
            _ => 0
        };
    }

    private int GetStreetRent()
    {
        if (RentValues.Length == 0)
        {
            return 0;
        }

        if (HasHotel)
        {
            return RentValues[5];
        }

        return HouseCount > 0 ? RentValues[HouseCount] : RentValues[0];
    }
}