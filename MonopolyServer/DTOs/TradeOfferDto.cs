namespace MonopolyServer.DTOs;

/// <summary>
/// Serialized trade offer sent to clients.
/// </summary>
public class TradeOfferDto
{
    /// <summary>
    /// Unique identifier for the trade offer.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the player who created the offer.
    /// </summary>
    public string FromPlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the player who created the offer.
    /// </summary>
    public string FromPlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the player to whom the offer is addressed.
    /// </summary>
    public string ToPlayerId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the player to whom the offer is addressed.
    /// </summary>
    public string ToPlayerName { get; set; } = string.Empty;

    /// <summary>
    /// IDs of properties included in the offer.
    /// </summary>
    public List<int> OfferedPropertyIds { get; set; } = new();

    /// <summary>
    /// Cash amount included in the offer.
    /// </summary>
    public int OfferedCash { get; set; }

    /// <summary>
    /// IDs of cards included in the offer.
    /// </summary>
    public List<string> OfferedCardIds { get; set; } = new();

    /// <summary>
    /// IDs of properties requested in exchange.
    /// </summary>
    public List<int> RequestedPropertyIds { get; set; } = new();

    /// <summary>
    /// Cash amount requested in exchange.
    /// </summary>
    public int RequestedCash { get; set; }

    /// <summary>
    /// IDs of cards requested in exchange.
    /// </summary>
    public List<string> RequestedCardIds { get; set; } = new();

    /// <summary>
    /// Current status of the offer (for example: pending, accepted, rejected).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the offer was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}