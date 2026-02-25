using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

/// <summary>
/// Represents the Monopoly game board and provides accessors for board spaces.
/// </summary>
public class Board
{
    /// <summary>
    /// Array of 40 board spaces (properties, utilities, taxes, etc.).
    /// </summary>
    public Property[] Spaces { get; }

    /// <summary>
    /// Construct a new Board and initialize all spaces.
    /// </summary>
    public Board()
    {
        Spaces = new Property[40];
        InitializeBoard();
    }

    /// <summary>
    /// Populate the board with the standard Monopoly layout.
    /// </summary>
    private void InitializeBoard()
    {
        // Row 1: Bottom (0-9)
        Spaces[0] = new Property(0, "Go", 0, PropertyType.Go);
        Spaces[1] = CreateStreet(1, "Mediterranean Avenue", "Brown", 60, 30, 50, 30, new[] { 2, 10, 30, 90, 160, 250 });
        Spaces[2] = new Property(2, "Community Chest", 2, PropertyType.CommunityChest);
        Spaces[3] = CreateStreet(3, "Baltic Avenue", "Brown", 60, 30, 50, 30, new[] { 4, 20, 60, 180, 320, 450 });
        Spaces[4] = new Property(4, "Income Tax", 4, PropertyType.Tax);
        Spaces[5] = new Property(5, "Reading Railroad", 5, 200, 100);
        Spaces[6] = CreateStreet(6, "Oriental Avenue", "Light Blue", 100, 50, 50, 30,
            new[] { 6, 30, 90, 270, 400, 550 });
        Spaces[7] = new Property(7, "Chance", 7, PropertyType.Chance);
        Spaces[8] = CreateStreet(8, "Vermont Avenue", "Light Blue", 100, 50, 50, 30,
            new[] { 6, 30, 90, 270, 400, 550 });
        Spaces[9] = CreateStreet(9, "Connecticut Avenue", "Light Blue", 120, 60, 50, 30,
            new[] { 8, 40, 120, 360, 640, 750 });

        // Row 2: Right side (10-19)
        Spaces[10] = new Property(10, "Jail", 10, PropertyType.Jail);
        Spaces[11] = CreateStreet(11, "St. Charles Place", "Pink", 140, 70, 100, 50,
            new[] { 10, 50, 150, 450, 625, 750 });
        Spaces[12] = new Property(12, "Electric Company", 12, PropertyType.Utility);
        Spaces[13] = CreateStreet(13, "States Avenue", "Pink", 140, 70, 100, 50, new[] { 10, 50, 150, 450, 625, 750 });
        Spaces[14] = CreateStreet(14, "Virginia Avenue", "Pink", 160, 80, 100, 50,
            new[] { 12, 60, 180, 500, 700, 900 });
        Spaces[15] = new Property(15, "Pennsylvania Railroad", 15, 200, 100);
        Spaces[16] = CreateStreet(16, "St. James Place", "Orange", 180, 90, 100, 50,
            new[] { 14, 70, 200, 550, 750, 950 });
        Spaces[17] = new Property(17, "Community Chest", 17, PropertyType.CommunityChest);
        Spaces[18] = CreateStreet(18, "Tennessee Avenue", "Orange", 180, 90, 100, 50,
            new[] { 14, 70, 200, 550, 750, 950 });
        Spaces[19] = CreateStreet(19, "New York Avenue", "Orange", 200, 100, 100, 50,
            new[] { 16, 80, 220, 600, 800, 1000 });

        // Row 3: Top (20-29)
        Spaces[20] = new Property(20, "Free Parking", 20, PropertyType.FreeParking);
        Spaces[21] = CreateStreet(21, "Kentucky Avenue", "Red", 220, 110, 150, 100,
            new[] { 18, 90, 250, 700, 875, 1050 });
        Spaces[22] = new Property(22, "Chance", 22, PropertyType.Chance);
        Spaces[23] = CreateStreet(23, "Indiana Avenue", "Red", 220, 110, 150, 100,
            new[] { 18, 90, 250, 700, 875, 1050 });
        Spaces[24] = CreateStreet(24, "Illinois Avenue", "Red", 240, 120, 150, 100,
            new[] { 20, 100, 300, 750, 925, 1100 });
        Spaces[25] = new Property(25, "B&O Railroad", 25, 200, 100);
        Spaces[26] = CreateStreet(26, "Atlantic Avenue", "Yellow", 260, 130, 150, 100,
            new[] { 22, 110, 330, 800, 975, 1150 });
        Spaces[27] = CreateStreet(27, "Ventnor Avenue", "Yellow", 260, 130, 150, 100,
            new[] { 22, 110, 330, 800, 975, 1150 });
        Spaces[28] = new Property(28, "Water Works", 28, PropertyType.Utility);
        Spaces[29] = CreateStreet(29, "Marvin Gardens", "Yellow", 280, 140, 150, 100,
            new[] { 24, 120, 360, 850, 1025, 1200 });

        // Row 4: Left side (30-39)
        Spaces[30] = new Property(30, "Go to Jail", 30, PropertyType.GoToJail);
        Spaces[31] = CreateStreet(31, "Pacific Avenue", "Green", 300, 150, 200, 150,
            new[] { 26, 130, 390, 900, 1100, 1275 });
        Spaces[32] = CreateStreet(32, "North Carolina Avenue", "Green", 300, 150, 200, 150,
            new[] { 26, 130, 390, 900, 1100, 1275 });
        Spaces[33] = new Property(33, "Community Chest", 33, PropertyType.CommunityChest);
        Spaces[34] = CreateStreet(34, "Pennsylvania Avenue", "Green", 320, 160, 200, 150,
            new[] { 28, 150, 450, 1000, 1200, 1400 });
        Spaces[35] = new Property(35, "Short Line", 35, 200, 100);
        Spaces[36] = new Property(36, "Chance", 36, PropertyType.Chance);
        Spaces[37] = CreateStreet(37, "Park Place", "Dark Blue", 350, 175, 200, 150,
            new[] { 35, 175, 500, 1100, 1300, 1500 });
        Spaces[38] = new Property(38, "Luxury Tax", 38, PropertyType.Tax);
        Spaces[39] = CreateStreet(39, "Boardwalk", "Dark Blue", 400, 200, 200, 150,
            new[] { 50, 200, 600, 1400, 1700, 2000 });
    }

    /// <summary>
    /// Helper to construct a street/property with typical parameters.
    /// </summary>
    private Property CreateStreet(int id, string name, string colorGroup, int purchasePrice,
        int mortgageValue, int houseCost, int hotelCost, int[] rentValues)
    {
        return new Property(id, name, id, purchasePrice, mortgageValue, houseCost, hotelCost, rentValues, colorGroup);
    }

    /// <summary>
    /// Get the property at the specified board position. Position is clamped to [0,39].
    /// </summary>
    public Property GetProperty(int position)
    {
        return Spaces[Math.Clamp(position, 0, 39)];
    }

    /// <summary>
    /// Return all properties currently owned by the specified player.
    /// </summary>
    public List<Property> GetPropertiesByOwner(string ownerId)
    {
        return Spaces.Where(p => p.OwnerId == ownerId).ToList();
    }

    /// <summary>
    /// Return all street properties in the given color group.
    /// </summary>
    public List<Property> GetPropertiesByColorGroup(string colorGroup)
    {
        return Spaces.Where(p => p.ColorGroup == colorGroup && p.Type == PropertyType.Street).ToList();
    }
}