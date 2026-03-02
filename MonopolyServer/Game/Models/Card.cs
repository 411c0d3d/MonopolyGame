using System.Text.Json.Serialization;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

/// <summary>
/// Represents a single Chance or Community Chest card.
/// </summary>
public class Card
{
    /// <summary>
    /// Unique identifier for the card.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Short title of the card (e.g. "Advance to Go").
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Descriptive text explaining the card's effect.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Categorizes the card by type (financial, movement, etc.).
    /// </summary>
    public CardType Type { get; set; }

    /// <summary>
    /// Which deck (Chance or Community Chest) this card belongs to.
    /// </summary>
    public CardDeck DeckType { get; set; }

    /// <summary>
    /// Monetary amount associated with the card, if applicable.
    /// </summary>
    public int? Amount { get; set; }

    /// <summary>
    /// Number of spaces to move forward (positive) or backward (negative), if applicable.
    /// </summary>
    public int? MoveSpaces { get; set; }

    /// <summary>
    /// Absolute board position to move the player to, if applicable.
    /// </summary>
    public int? TargetPosition { get; set; }

    /// <summary>
    /// Cost per house when the card requires house repairs, if applicable.
    /// </summary>
    public int? HouseRepairCost { get; set; }

    /// <summary>
    /// Cost per hotel when the card requires hotel repairs, if applicable.
    /// </summary>
    public int? HotelRepairCost { get; set; }

    /// <summary>
    /// Indicates whether this card can be kept by a player (e.g., Get Out of Jail Free).
    /// </summary>
    public bool IsKeptByPlayer { get; set; }

    /// <summary>
    /// Initialize a new Card instance with required identity and deck information.
    /// </summary>
    [JsonConstructor]
    public Card(string id, string title, string description, CardType type, CardDeck deckType)
    {
        Id = id;
        Title = title;
        Description = description;
        Type = type;
        DeckType = deckType;
        IsKeptByPlayer = false;
    }

    /// <summary>
    /// Returns a short string describing the card for logging and debug output.
    /// </summary>
    public override string ToString() => $"{DeckType} - {Title}";
}