using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Bot;

/// <summary>
/// Stateless decision engine for bot players. All decisions are deterministic and based on
/// cash thresholds: conservative below $400, normal $400–$800, aggressive above $800.
/// </summary>
public class BotDecisionEngine
{
    /// <summary>
    /// Minimum cash reserve — double the Go bonus. Stay above this at all times.
    /// </summary>
    public const int SafetyThreshold = 400;

    /// <summary>
    /// Cash level above which the bot builds and bails out of jail proactively.
    /// </summary>
    public const int AggressiveThreshold = 800;

    private const int MinCashAfterPurchase = SafetyThreshold;
    private const int MinCashAfterPurchaseForMonopoly = 200;
    private const int MinCashAfterBuilding = AggressiveThreshold - 200;
    private const double TradePropertyPremium = 1.20; // Offer 20% above purchase price to acquire a missing property
    private const double TradeAcceptMargin = 1.10;    // Accept if receiving >= 10% more value than giving

    // -------------------------------------------------------------------------
    // Property buying
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the bot should purchase the property it landed on.
    /// </summary>
    public bool ShouldBuyProperty(Player bot, Property property, GameState state)
    {
        int cashAfter = bot.Cash - property.PurchasePrice;

        if (WouldCompleteMonopoly(bot, property, state))
        {
            return cashAfter >= MinCashAfterPurchaseForMonopoly;
        }

        return cashAfter >= MinCashAfterPurchase;
    }

    // -------------------------------------------------------------------------
    // Building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the bot should build a house on this property.
    /// </summary>
    public bool ShouldBuildHouse(Player bot, Property property, GameState state)
    {
        if (bot.Cash < AggressiveThreshold) { return false; }
        if (property.HouseCount >= GameConstants.MaxHousesPerProperty || property.HasHotel) { return false; }
        if (property.ColorGroup == null) { return false; }

        var colorGroup = state.Board.GetPropertiesByColorGroup(property.ColorGroup).ToList();
        if (colorGroup.Any(p => p.OwnerId != bot.Id)) { return false; }

        // Even-build rule: never advance ahead of a sibling that has fewer houses
        if (colorGroup.Any(p => p.Id != property.Id && !p.HasHotel && p.HouseCount < property.HouseCount))
        {
            return false;
        }

        return bot.Cash - property.HouseCost >= MinCashAfterBuilding;
    }

    /// <summary>
    /// Returns true if the bot should upgrade this property to a hotel.
    /// </summary>
    public bool ShouldBuildHotel(Player bot, Property property, GameState state)
    {
        if (bot.Cash < AggressiveThreshold) { return false; }
        if (property.HouseCount < 4 || property.HasHotel) { return false; }

        return bot.Cash - property.HotelCost >= MinCashAfterBuilding;
    }

    // -------------------------------------------------------------------------
    // Jail
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the preferred jail exit strategy given the bot's current cash.
    /// </summary>
    public JailStrategy GetJailStrategy(Player bot)
    {
        if (bot.KeptCards.Any(c => c.Type == CardType.GetOutOfJailFree))
        {
            return JailStrategy.UseCard;
        }

        if (bot.Cash > AggressiveThreshold && bot.Cash - GameConstants.JailBailCost > SafetyThreshold)
        {
            return JailStrategy.PayBail;
        }

        return JailStrategy.Roll;
    }

    // -------------------------------------------------------------------------
    // Mortgage management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the bot should proactively mortgage this property to raise cash.
    /// </summary>
    public bool ShouldMortgageProactively(Player bot, Property property)
    {
        return bot.Cash < 300
            && !property.IsMortgaged
            && property.HouseCount == 0
            && !property.HasHotel;
    }

    /// <summary>
    /// Returns true if the bot should lift the mortgage on this property now.
    /// </summary>
    public bool ShouldUnmortgage(Player bot, Property property)
    {
        if (!property.IsMortgaged) { return false; }

        int cost = (int)(property.MortgageValue * 1.1);
        return bot.Cash - cost >= AggressiveThreshold;
    }

    // -------------------------------------------------------------------------
    // Trade proposals (outgoing)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates trade proposals targeting color groups where the bot owns all but one property.
    /// Returns at most one proposal per target player and avoids duplicate pending trades.
    /// </summary>
    public List<TradeOffer> GenerateTradeProposals(Player bot, GameState state)
    {
        var proposals = new List<TradeOffer>();

        // Don't pile on — wait for any open offer to resolve first
        if (state.PendingTrades.Any(t => t.FromPlayerId == bot.Id && t.Status == TradeStatus.Pending))
        {
            return proposals;
        }

        var alreadyTargeted = new HashSet<string>();

        var nearCompleteGroups = state.Board.GetPropertiesByOwner(bot.Id)
            .Where(p => p.ColorGroup != null)
            .GroupBy(p => p.ColorGroup!)
            .Select(g => new
            {
                ColorGroup = g.Key,
                FullGroup = state.Board.GetPropertiesByColorGroup(g.Key).ToList()
            })
            .Where(g => g.FullGroup.Count(p => p.OwnerId != bot.Id && p.OwnerId != null) == 1);

        foreach (var group in nearCompleteGroups)
        {
            var missing = group.FullGroup.First(p => p.OwnerId != null && p.OwnerId != bot.Id);
            if (alreadyTargeted.Contains(missing.OwnerId!)) { continue; }

            var targetPlayer = state.GetPlayerById(missing.OwnerId!);
            if (targetPlayer == null || targetPlayer.IsBankrupt) { continue; }

            var offerableProp = FindOfferableProperty(bot, state, group.ColorGroup);

            // Total offer value targets a premium on the missing property's purchase price
            int totalOfferValue = (int)(missing.PurchasePrice * TradePropertyPremium);

            // Offset by the offered property's value; remainder comes from cash
            int cashPortion = offerableProp != null
                ? Math.Max(0, totalOfferValue - offerableProp.PurchasePrice)
                : totalOfferValue;

            if (bot.Cash - cashPortion < SafetyThreshold) { continue; }

            var offer = new TradeOffer(
                fromPlayerId: bot.Id,
                toPlayerId: missing.OwnerId!,
                offeredPropertyIds: offerableProp != null ? [offerableProp.Id] : [],
                offeredCash: cashPortion,
                offeredCardIds: [],
                requestedPropertyIds: [missing.Id],
                requestedCash: 0,
                requestedCardIds: []
            );

            proposals.Add(offer);

            alreadyTargeted.Add(missing.OwnerId!);
        }

        return proposals;
    }

    // -------------------------------------------------------------------------
    // Trade evaluation (incoming)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Evaluates an incoming trade offer and returns true if the bot should accept it.
    /// </summary>
    public bool ShouldAcceptTrade(TradeOffer trade, Player bot, GameState state)
    {
        int cashAfterTrade = bot.Cash + trade.OfferedCash - trade.RequestedCash;
        if (cashAfterTrade < SafetyThreshold) { return false; }

        int receiveValue = trade.OfferedCash;
        foreach (var propId in trade.OfferedPropertyIds)
        {
            var prop = state.Board.GetProperty(propId);
            receiveValue += prop.PurchasePrice;

            // Monopoly completion always justifies acceptance regardless of raw value
            if (WouldCompleteMonopoly(bot, prop, state)) { return true; }
        }

        int giveValue = trade.RequestedCash;
        foreach (var propId in trade.RequestedPropertyIds)
        {
            giveValue += state.Board.GetProperty(propId).PurchasePrice;
        }

        return receiveValue >= (int)(giveValue * TradeAcceptMargin);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private bool WouldCompleteMonopoly(Player bot, Property property, GameState state)
    {
        if (property.ColorGroup == null) { return false; }

        return state.Board.GetPropertiesByColorGroup(property.ColorGroup)
            .All(p => p.Id == property.Id || p.OwnerId == bot.Id);
    }

    /// <summary>
    /// Finds the least valuable property the bot can offer without breaking an existing monopoly.
    /// </summary>
    private Property? FindOfferableProperty(Player bot, GameState state, string excludeColorGroup)
    {
        return state.Board.GetPropertiesByOwner(bot.Id)
            .Where(p =>
                p.ColorGroup != excludeColorGroup &&
                p.HouseCount == 0 &&
                !p.HasHotel &&
                !p.IsMortgaged)
            .Where(p =>
            {
                // Don't offer a property that's part of a complete monopoly — it would break it
                if (p.ColorGroup == null) { return true; }
                var group = state.Board.GetPropertiesByColorGroup(p.ColorGroup);
                return group.Any(x => x.OwnerId != bot.Id);
            })
            .OrderBy(p => p.PurchasePrice)
            .FirstOrDefault();
    }
}