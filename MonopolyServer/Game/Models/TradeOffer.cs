using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Models;

/// <summary>
/// Represents a trade offer between two players in a game.
/// </summary>
public class TradeOffer
{
    /// <summary>
    /// Unique identifier for the trade offer.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The player id who created / sent the offer.
    /// </summary>
    public string FromPlayerId { get; set; }

    /// <summary>
    /// The player id who is the recipient of the offer.
    /// </summary>
    public string ToPlayerId { get; set; }

    /// <summary>
    /// List of property ids included in the offer.
    /// </summary>
    public List<int> OfferedPropertyIds { get; set; }

    /// <summary>
    /// Amount of cash included in the offer.
    /// </summary>
    public int OfferedCash { get; set; }

    /// <summary>
    /// List of card ids included in the offer.
    /// </summary>
    public List<string> OfferedCardIds { get; set; }

    /// <summary>
    /// List of property ids requested in return.
    /// </summary>
    public List<int> RequestedPropertyIds { get; set; }

    /// <summary>
    /// Amount of cash requested in return.
    /// </summary>
    public int RequestedCash { get; set; }

    /// <summary>
    /// List of card ids requested in return.
    /// </summary>
    public List<string> RequestedCardIds { get; set; }

    /// <summary>
    /// Current status of the trade offer (Pending, Accepted, Rejected, Cancelled).
    /// </summary>
    public TradeStatus Status { get; set; }

    /// <summary>
    /// Timestamp when the offer was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the offer was responded to; null if not yet responded.
    /// </summary>
    public DateTime? RespondedAt { get; set; }


    /// <summary>
    /// Constructor to create a new trade offer with the specified details. The offer is initialized with a unique ID, current timestamp, and a default status of Pending.
    /// </summary>
    /// <param name="fromPlayerId">ID of the player who created/sent the offer.</param>
    /// <param name="toPlayerId">ID of the player who is the recipient of the offer.</param>
    /// <param name="offeredPropertyIds">List of property IDs included in the offer from the sender.</param>
    /// <param name="offeredCash">Amount of cash (in game currency) included in the offer from the sender.</param>
    /// <param name="offeredCardIds">List of card IDs (e.g., Chance/Community Chest) included in the offer from the sender.</param>
    /// <param name="requestedPropertyIds">List of property IDs requested from the recipient in exchange.</param>
    /// <param name="requestedCash">Amount of cash requested from the recipient in exchange.</param>
    /// <param name="requestedCardIds">List of card IDs requested from the recipient in exchange.</param>
    public TradeOffer(
        string fromPlayerId,
        string toPlayerId,
        List<int> offeredPropertyIds,
        int offeredCash,
        List<string> offeredCardIds,
        List<int> requestedPropertyIds,
        int requestedCash,
        List<string> requestedCardIds)
    {
        Id = Guid.NewGuid().ToString();
        FromPlayerId = fromPlayerId;
        ToPlayerId = toPlayerId;
        OfferedPropertyIds = offeredPropertyIds;
        OfferedCash = offeredCash;
        OfferedCardIds = offeredCardIds;
        RequestedPropertyIds = requestedPropertyIds;
        RequestedCash = requestedCash;
        RequestedCardIds = requestedCardIds;
        CreatedAt = DateTime.UtcNow;
        Status = TradeStatus.Pending;
    }
}