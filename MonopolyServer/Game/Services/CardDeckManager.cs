using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Manages the Chance and Community Chest card decks.
/// Handles shuffling, drawing, returning cards to the bottom of the deck.
/// </summary>
public class CardDeckManager
{
    private Queue<Card> _chanceCards;
    private Queue<Card> _communityChestCards;
    private List<Card> _allChanceCards;
    private List<Card> _allCommunityChestCards;
    private Random _random;

    /// <summary>
    /// Constructor initializes the card decks and shuffles them.
    /// </summary>
    public CardDeckManager()
    {
        _random = new Random();
        _allChanceCards = InitializeChanceCards();
        _allCommunityChestCards = InitializeCommunityChestCards();
        _chanceCards = new Queue<Card>(Shuffle(_allChanceCards));
        _communityChestCards = new Queue<Card>(Shuffle(_allCommunityChestCards));
    }

    /// <summary>
    /// Initialize all 16 classic Chance cards.
    /// </summary>
    private List<Card> InitializeChanceCards()
    {
        var cards = new List<Card>
        {
            new("chance_1", "Advance to Go", "Advance to Go (Collect $200)", CardType.MoveToGo, CardDeck.Chance)
            {
                TargetPosition = 0
            },

            new("chance_2", "Advance to Illinois Avenue", "Advance to Illinois Avenue", CardType.MoveToSpecificLocation,
                CardDeck.Chance)
            {
                TargetPosition = 24 // Illinois Avenue
            },

            new("chance_3", "Advance to St. Charles Place",
                "Advance to St. Charles Place (If you pass Go, collect $200)", CardType.MoveToSpecificLocation,
                CardDeck.Chance)
            {
                TargetPosition = 11 // St. Charles Place
            },

            new("chance_4", "Advance to nearest Utility", "Advance to the nearest Utility", CardType.Advance,
                CardDeck.Chance),

            new("chance_5", "Advance to nearest Railroad", "Advance to the nearest Railroad", CardType.Advance,
                CardDeck.Chance),

            new("chance_6", "Advance to nearest Railroad",
                "Advance to the nearest Railroad (If you pass Go, collect $200)", CardType.Advance, CardDeck.Chance),

            new("chance_7", "Bank pays you dividend", "Bank pays you a dividend of $50", CardType.CollectFromBank,
                CardDeck.Chance)
            {
                Amount = 50
            },

            new("chance_8", "Go Back 3 Spaces", "Go back 3 spaces", CardType.MoveBackward, CardDeck.Chance)
            {
                MoveSpaces = -3
            },

            new("chance_9", "Go to Jail", "Go to Jail. Do not pass Go, do not collect $200", CardType.MoveToJail,
                CardDeck.Chance)
            {
                TargetPosition = 10
            },

            new("chance_10", "Get Out of Jail Free", "Get Out of Jail Free (Keep this card)", CardType.GetOutOfJailFree,
                CardDeck.Chance)
            {
                IsKeptByPlayer = true
            },

            new("chance_11", "Make general repairs",
                "Make general repairs on all your properties: For each house pay $25, For each hotel pay $100",
                CardType.PayForHouseRepairs, CardDeck.Chance)
            {
                HouseRepairCost = 25,
                HotelRepairCost = 100
            },

            new("chance_12", "Pay poor tax", "Pay poor tax of $15", CardType.PayBank, CardDeck.Chance)
            {
                Amount = 15
            },

            new("chance_13", "Take a trip to Reading Railroad", "Take a trip to Reading Railroad",
                CardType.MoveToSpecificLocation, CardDeck.Chance)
            {
                TargetPosition = 5 // Reading Railroad
            },

            new("chance_14", "Take a walk on the Boardwalk", "Take a walk on the Boardwalk",
                CardType.MoveToSpecificLocation, CardDeck.Chance)
            {
                TargetPosition = 39 // Boardwalk
            },

            new("chance_15", "You are elected Chairman", "You are elected Chairman of the Board. Pay each player $50",
                CardType.PayEachPlayer, CardDeck.Chance)
            {
                Amount = 50
            },

            new("chance_16", "Your building loan matures", "Your building loan matures. Collect $150",
                CardType.CollectFromBank, CardDeck.Chance)
            {
                Amount = 150
            }
        };

        return cards;
    }

    /// <summary>
    /// Initialize all 16 classic Community Chest cards.
    /// </summary>
    private List<Card> InitializeCommunityChestCards()
    {
        var cards = new List<Card>
        {
            new("cc_1", "Advance to Go", "Advance to Go (Collect $200)", CardType.MoveToGo, CardDeck.CommunityChest)
            {
                TargetPosition = 0
            },

            new("cc_2", "Bank error in your favor", "Bank error in your favor. Collect $200", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 200
            },

            new("cc_3", "Doctor's fee", "Doctor's fee. Pay $50", CardType.PayBank, CardDeck.CommunityChest)
            {
                Amount = 50
            },

            new("cc_4", "From sale of stock", "From sale of stock you have $45", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 45
            },

            new("cc_5", "Get Out of Jail Free", "Get Out of Jail Free (Keep this card)", CardType.GetOutOfJailFree,
                CardDeck.CommunityChest)
            {
                IsKeptByPlayer = true
            },

            new("cc_6", "Go to Jail", "Go to Jail. Do not pass Go, do not collect $200", CardType.MoveToJail,
                CardDeck.CommunityChest)
            {
                TargetPosition = 10
            },

            new("cc_7", "Holiday fund matures", "Holiday fund matures. Collect $100", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 100
            },

            new("cc_8", "Income tax refund", "Income tax refund. Collect $20", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 20
            },

            new("cc_9", "It is your birthday", "It is your birthday. Collect $10 from each player",
                CardType.CollectFromEachPlayer, CardDeck.CommunityChest)
            {
                Amount = 10
            },

            new("cc_10", "Life insurance matures", "Life insurance matures. Collect $100", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 100
            },

            new("cc_11", "Pay hospital fees", "Pay hospital fees of $50", CardType.PayBank, CardDeck.CommunityChest)
            {
                Amount = 50
            },

            new("cc_12", "Pay school fees", "Pay school fees of $50", CardType.PayBank, CardDeck.CommunityChest)
            {
                Amount = 50
            },

            new("cc_13", "Receive $25 consultancy fee", "Receive $25 consultancy fee", CardType.CollectFromBank,
                CardDeck.CommunityChest)
            {
                Amount = 25
            },

            new("cc_14", "Street repairs", "You are assessed for street repairs: $40 per house, $115 per hotel",
                CardType.PayForHouseRepairs, CardDeck.CommunityChest)
            {
                HouseRepairCost = 40,
                HotelRepairCost = 115
            },

            new("cc_15", "Taxes due", "Taxes due. Pay $100", CardType.PayBank, CardDeck.CommunityChest)
            {
                Amount = 100
            },

            new("cc_16", "You have won a crossword competition", "You have won a crossword competition. Collect $100",
                CardType.CollectFromBank, CardDeck.CommunityChest)
            {
                Amount = 100
            }
        };

        return cards;
    }

    /// <summary>
    /// Draw a card from the specified deck.
    /// </summary>
    public Card DrawCard(CardDeck deck)
    {
        var queue = deck == CardDeck.Chance ? _chanceCards : _communityChestCards;

        if (queue.Count == 0)
        {
            // Reshuffle if deck is empty (shouldn't happen often, but safety)
            var allCards = deck == CardDeck.Chance ? _allChanceCards : _allCommunityChestCards;
            queue = new Queue<Card>(Shuffle(allCards));
            if (deck == CardDeck.Chance)
                _chanceCards = queue;
            else
                _communityChestCards = queue;
        }

        return queue.Dequeue();
    }

    /// <summary>
    /// Return a card to the bottom of its deck (after being played).
    /// Used for cards that aren't kept by the player.
    /// </summary>
    public void ReturnCardToBottom(Card card)
    {
        var queue = card.DeckType == CardDeck.Chance ? _chanceCards : _communityChestCards;
        queue.Enqueue(card);
    }

    /// <summary>
    /// Shuffle a list and return a new shuffled list.
    /// </summary>
    private List<Card> Shuffle(List<Card> cards)
    {
        var shuffled = new List<Card>(cards);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int randomIndex = _random.Next(i + 1);
            (shuffled[i], shuffled[randomIndex]) = (shuffled[randomIndex], shuffled[i]);
        }

        return shuffled;
    }

    /// <summary>
    /// Get the current size of a deck (for debugging/UI).
    /// </summary>
    public int GetDeckSize(CardDeck deck)
    {
        return deck == CardDeck.Chance ? _chanceCards.Count : _communityChestCards.Count;
    }
}