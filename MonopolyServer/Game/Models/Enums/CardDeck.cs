namespace MonopolyServer.Game.Models.Enums;

/// <summary>
/// Which deck a card belongs to.
/// </summary>
public enum CardDeck
{
    /// <summary>
    /// Chance deck (typically contains movement and payment cards).
    /// </summary>
    Chance,

    /// <summary>
    /// Community Chest deck (typically contains smaller rewards/penalties and Get Out of Jail Free).
    /// </summary>
    CommunityChest
}