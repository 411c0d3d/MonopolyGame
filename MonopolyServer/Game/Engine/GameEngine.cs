using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Game.Engine;

/// <summary>
/// Core game engine for Monopoly. Contains all business logic.
/// Works exclusively with GameState (data model).
/// </summary>
public class GameEngine
{
    private readonly GameState _state;
    private readonly CardDeckManager _cardDeckManager;
    private static readonly Random _random = new Random();

    public GameEngine(GameState gameState, CardDeckManager? cardDeckManager = null)
    {
        _state = gameState;
        _cardDeckManager = cardDeckManager ?? new CardDeckManager();
    }

    /// <summary>
    /// Start the game. Called when host clicks "Start Game" and all players are ready.
    /// </summary>
    public void StartGame()
    {
        if (_state.Players.Count < 2)
        {
            throw new InvalidOperationException("Need at least 2 players to start.");
        }

        _state.Status = GameStatus.InProgress;
        _state.StartedAt = DateTime.UtcNow;
        _state.CurrentPlayerIndex = 0;
        _state.Turn = 1;

        var firstPlayer = _state.GetCurrentPlayer();
        if (firstPlayer != null)
        {
            firstPlayer.IsCurrentPlayer = true;
            _state.LogAction($"Game started! {firstPlayer.Name} goes first.");
        }
    }

    /// <summary>
    /// Roll the dice. Tracks consecutive doubles and sends player to jail on the third.
    /// Returns whether the player was sent to jail so the hub can skip movement.
    /// </summary>
    public (int Dice1, int Dice2, int Total, bool IsDouble, bool SentToJail) RollDice()
    {
        int dice1 = _random.Next(1, 7);
        int dice2 = _random.Next(1, 7);
        int total = dice1 + dice2;
        bool isDouble = dice1 == dice2;
        bool sentToJail = false;

        _state.LastDiceRoll = total;
        _state.DoubleRolled = isDouble;

        var currentPlayer = _state.GetCurrentPlayer();
        if (currentPlayer != null)
        {
            currentPlayer.LastDiceRoll = total;
            currentPlayer.HasRolledDice = true;

            if (isDouble)
            {
                currentPlayer.ConsecutiveDoubles++;

                if (currentPlayer.ConsecutiveDoubles >= 3)
                {
                    SendPlayerToJail(currentPlayer);
                    sentToJail = true;
                    _state.LogAction($"{currentPlayer.Name} rolled three consecutive doubles and was sent to jail!");
                }
                else
                {
                    _state.LogAction($"{currentPlayer.Name} rolled: {dice1} + {dice2} = {total} (Double! Roll again)");
                }
            }
            else
            {
                currentPlayer.ConsecutiveDoubles = 0;
                _state.LogAction($"{currentPlayer.Name} rolled: {dice1} + {dice2} = {total}");
            }
        }

        return (dice1, dice2, total, isDouble, sentToJail);
    }

    /// <summary>
    /// Move the current player by the dice roll amount. Handle Go, jail, landing on properties, etc.
    /// </summary>
    public void MovePlayer(int diceTotal)
    {
        var player = _state.GetCurrentPlayer();
        if (player == null)
        {
            return;
        }

        // Ensure result is always positive even with negative diceTotal
        int newPosition = (player.Position + diceTotal + 40) % 40;

        // Passing or landing on Go (only for forward movement)
        if (diceTotal > 0 && newPosition < player.Position)
        {
            player.AddCash(Constants.GameConstants.GoPassingBonus);
            _state.LogAction($"{player.Name} passed Go and collected $200.");
        }

        player.MoveTo(newPosition);

        // Handle the space they landed on
        HandleLandingOnSpace(player, newPosition);
    }

    /// <summary>
    /// Handle what happens when a player lands on a space.
    /// </summary>
    private void HandleLandingOnSpace(Player player, int position)
    {
        var space = _state.Board.GetProperty(position);

        _state.LogAction($"{player.Name} landed on {space.Name}.");

        switch (space.Type)
        {
            case PropertyType.Street:
            case PropertyType.Railroad:
            case PropertyType.Utility:
                HandlePropertyLanding(player, space);
                break;

            case PropertyType.GoToJail:
                SendPlayerToJail(player);
                break;

            case PropertyType.Jail:
                _state.LogAction($"{player.Name} is visiting Jail (not in jail).");
                break;

            case PropertyType.Tax:
                HandleTax(player, space);
                break;

            case PropertyType.Chance:
                _state.LogAction($"{player.Name} drew a Chance card (not implemented).");
                break;

            case PropertyType.CommunityChest:
                _state.LogAction($"{player.Name} drew a Community Chest card (not implemented).");
                break;

            case PropertyType.FreeParking:
                _state.LogAction($"{player.Name} is in Free Parking.");
                break;

            case PropertyType.Go:
                _state.LogAction($"{player.Name} is at Go.");
                break;
        }
    }

    /// <summary>
    /// Handle landing on a property (street, railroad, utility).
    /// If owned by another player, collect rent and attribute any bankruptcy to that creditor.
    /// </summary>
    private void HandlePropertyLanding(Player player, Property property)
    {
        if (property.OwnerId == null)
        {
            _state.LogAction($"{property.Name} is unowned. {player.Name} can buy it for ${property.PurchasePrice}.");
        }
        else if (property.OwnerId == player.Id)
        {
            _state.LogAction($"{player.Name} owns {property.Name}.");
        }
        else
        {
            // Pay rent to owner
            var owner = _state.GetPlayerById(property.OwnerId);
            if (owner != null)
            {
                int rent = CalculateRent(property, _state.LastDiceRoll);
                CollectRent(player, owner, rent, property.Name);
            }
        }
    }

    /// <summary>
    /// Calculate rent owed on a property based on its state (houses, hotels, ownership).
    /// </summary>
    private int CalculateRent(Property property, int diceRoll)
    {
        if (property.IsMortgaged || property.OwnerId == null)
            return 0;

        return property.Type switch
        {
            PropertyType.Street => CalculateStreetRent(property),
            PropertyType.Railroad => CalculateRailroadRent(property),
            PropertyType.Utility => CalculateUtilityRent(property, diceRoll),
            _ => 0
        };
    }

    /// <summary>
    /// Compute rent for a street property: base, monopoly-doubled, or by house/hotel level.
    /// </summary>
    private int CalculateStreetRent(Property property)
    {
        if (property.RentValues.Length == 0)
            return 0;

        if (property.HasHotel)
            return property.RentValues[5];

        if (property.HouseCount > 0)
            return property.RentValues[property.HouseCount];

        var colorGroup = _state.Board.GetPropertiesByColorGroup(property.ColorGroup!);
        bool hasMonopoly = colorGroup.All(p => p.OwnerId == property.OwnerId);
        return hasMonopoly ? property.RentValues[0] * 2 : property.RentValues[0];
    }

    /// <summary>
    /// Compute rent for a railroad: 25/50/100/200 based on number owned.
    /// </summary>
    private int CalculateRailroadRent(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null)
            return 0;

        int railroadsOwned = _state.Board.Spaces.Count(p =>
            p.Type == PropertyType.Railroad && p.OwnerId == owner.Id && !p.IsMortgaged);

        return 25 * (int)Math.Pow(2, railroadsOwned - 1);
    }

    /// <summary>
    /// Compute rent for a utility: 4x or 10x dice roll based on number owned.
    /// </summary>
    private int CalculateUtilityRent(Property property, int diceRoll)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null)
            return 0;

        int utilitiesOwned = _state.Board.Spaces.Count(p =>
            p.Type == PropertyType.Utility && p.OwnerId == owner.Id && !p.IsMortgaged);

        return diceRoll * (utilitiesOwned == 1 ? 4 : 10);
    }

    /// <summary>
    /// Collect rent from payer to owner. If payer cannot cover rent, all remaining cash
    /// goes to the owner and the payer is bankrupted with the owner as creditor.
    /// </summary>
    private void CollectRent(Player payer, Player owner, int amount, string propertyName)
    {
        if (amount <= 0)
            return;

        if (payer.Cash >= amount)
        {
            payer.DeductCash(amount);
            owner.AddCash(amount);
            _state.LogAction($"{payer.Name} paid ${amount} rent to {owner.Name} for {propertyName}.");
        }
        else
        {
            // Payer goes bankrupt
            int cashAvailable = payer.Cash;
            payer.DeductCash(cashAvailable);
            owner.AddCash(cashAvailable);
            _state.LogAction($"{payer.Name} owed ${amount} but only had ${cashAvailable}. {payer.Name} is bankrupt!");
            BankruptPlayer(payer, creditor: owner);
        }
    }

    /// <summary>
    /// Handle income tax or luxury tax. Insufficient funds bankrupts to the bank (no creditor).
    /// </summary>
    private void HandleTax(Player player, Property space)
    {
        int taxAmount = space.Name.Contains("Income") ? 200 : 100;
        if (player.DeductCash(taxAmount))
        {
            _state.LogAction($"{player.Name} paid ${taxAmount} in taxes.");
        }
        else
        {
            int available = player.Cash;
            player.DeductCash(available);
            _state.LogAction($"{player.Name} couldn't afford ${taxAmount} in taxes. {player.Name} is bankrupt!");
            BankruptPlayer(player, creditor: null);
        }
    }

    /// <summary>
    /// Send player to jail. Clear any existing jail turns and move them.
    /// </summary>
    public void SendPlayerToJail(Player player)
    {
        player.SendToJail();
        _state.LogAction($"{player.Name} was sent to jail!");
    }

    /// <summary>
    /// Attempt to release player from jail by rolling doubles, exhausting turns, or paying $50 bail.
    /// </summary>
    public bool ReleaseFromJail(Player player, bool payToBail = false)
    {
        if (!player.IsInJail)
            return false;

        if (payToBail)
        {
            if (player.DeductCash(50))
            {
                player.ReleaseFromJail();
                _state.LogAction($"{player.Name} paid $50 to leave jail.");
                return true;
            }

            return false;
        }

        // If they rolled doubles or served 3 turns
        player.JailTurnsRemaining--;
        if (player.JailTurnsRemaining <= 0 || _state.DoubleRolled)
        {
            player.ReleaseFromJail();
            _state.LogAction($"{player.Name} was released from jail.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Pay $50 bail to leave jail immediately before rolling. Returns false if player cannot afford it.
    /// </summary>
    public bool PayBailToLeaveJail(Player player)
    {
        return ReleaseFromJail(player, payToBail: true);
    }

    /// <summary>
    /// Mark a player as bankrupt. If a creditor is provided (rent bankruptcy), all properties,
    /// remaining cash, and kept cards transfer to that creditor. Otherwise (tax, card-triggered),
    /// properties return to market and kept cards return to their decks.
    /// </summary>
    public void BankruptPlayer(Player player, Player? creditor)
    {
        var properties = _state.Board.Spaces.Where(p => p.OwnerId == player.Id).ToList();

        if (creditor != null)
        {
            creditor.AddCash(player.Cash);

            foreach (var property in properties)
            {
                property.OwnerId = creditor.Id;
                property.IsMortgaged = false;
                property.HouseCount = 0;
                property.HasHotel = false;
            }

            foreach (var card in player.KeptCards)
            {
                creditor.KeptCards.Add(card);
            }

            _state.LogAction($"{player.Name} is bankrupt. All assets transferred to {creditor.Name}.");
        }
        else
        {
            foreach (var property in properties)
            {
                property.OwnerId = null;
                property.IsMortgaged = false;
                property.HouseCount = 0;
                property.HasHotel = false;
            }

            foreach (var card in player.KeptCards)
            {
                _cardDeckManager.ReturnCardToBottom(card);
            }

            _state.LogAction($"{player.Name} is bankrupt. All assets returned to the bank.");
        }

        player.KeptCards.Clear();
        player.Bankrupt();

        var activePlayers = _state.Players.Where(p => !p.IsBankrupt).ToList();
        if (activePlayers.Count == 1)
        {
            EndGame(activePlayers[0]);
        }
    }

    /// <summary>
    /// End the game with a winner.
    /// </summary>
    private void EndGame(Player winner)
    {
        _state.Status = GameStatus.Finished;
        _state.EndedAt = DateTime.UtcNow;
        _state.LogAction($"Game Over! {winner.Name} wins with ${winner.Cash}!");
    }

    /// <summary>
    /// Advance to the next player's turn. Resets per-turn state including consecutive doubles.
    /// </summary>
    public void NextTurn()
    {
        var currentPlayer = _state.GetCurrentPlayer();
        if (currentPlayer != null)
        {
            currentPlayer.IsCurrentPlayer = false;
            currentPlayer.HasRolledDice = false;
            currentPlayer.ConsecutiveDoubles = 0;
        }

        // Skip bankrupt players
        int attempts = 0;
        do
        {
            _state.CurrentPlayerIndex = (_state.CurrentPlayerIndex + 1) % _state.Players.Count;
            attempts++;
        } while (attempts < _state.Players.Count && _state.GetCurrentPlayer()?.IsBankrupt == true);

        _state.Turn++;

        var nextPlayer = _state.GetCurrentPlayer();
        if (nextPlayer != null)
        {
            nextPlayer.IsCurrentPlayer = true;
            _state.LogAction($"--- Turn {_state.Turn}: {nextPlayer.Name}'s turn ---");
        }
    }

    /// <summary>
    /// Buy a property for the current player.
    /// </summary>
    public bool BuyProperty(Property property)
    {
        var player = _state.GetCurrentPlayer();
        if (player == null || property.OwnerId != null)
            return false;

        if (player.DeductCash(property.PurchasePrice))
        {
            property.OwnerId = player.Id;
            _state.LogAction($"{player.Name} bought {property.Name} for ${property.PurchasePrice}.");
            return true;
        }

        _state.LogAction($"{player.Name} can't afford {property.Name}.");
        return false;
    }

    /// <summary>
    /// Decline to purchase the property the current player is standing on. Property remains unowned.
    /// </summary>
    public bool DeclineProperty(Property property)
    {
        var player = _state.GetCurrentPlayer();
        if (player == null || property.OwnerId != null)
            return false;

        _state.LogAction($"{player.Name} declined to buy {property.Name}.");
        return true;
    }

    /// <summary>
    /// Build a house on a property (requires monopoly and sufficient cash).
    /// </summary>
    public bool BuildHouse(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || property.HouseCount >= 4 || property.HasHotel)
            return false;

        // Check monopoly
        var colorGroup = _state.Board.GetPropertiesByColorGroup(property.ColorGroup!);
        if (!colorGroup.All(p => p.OwnerId == owner.Id))
            return false;

        if (owner.DeductCash(property.HouseCost))
        {
            property.HouseCount++;
            _state.LogAction($"{owner.Name} built a house on {property.Name}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Build a hotel on a property (requires 4 houses and sufficient cash).
    /// </summary>
    public bool BuildHotel(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || property.HouseCount < 4 || property.HasHotel)
            return false;

        if (owner.DeductCash(property.HotelCost))
        {
            property.HouseCount = 0;
            property.HasHotel = true;
            _state.LogAction($"{owner.Name} built a hotel on {property.Name}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sell one house from a property back to the bank for half the house cost.
    /// </summary>
    public bool SellHouse(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || property.HouseCount <= 0 || property.HasHotel)
            return false;

        int refund = property.HouseCost / 2;
        property.HouseCount--;
        owner.AddCash(refund);
        _state.LogAction($"{owner.Name} sold a house on {property.Name} for ${refund}.");
        return true;
    }

    /// <summary>
    /// Sell a hotel from a property back to the bank for half the hotel cost.
    /// Hotel is removed and the property returns to zero houses.
    /// </summary>
    public bool SellHotel(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || !property.HasHotel)
            return false;

        int refund = property.HotelCost / 2;
        property.HasHotel = false;
        property.HouseCount = 0;
        owner.AddCash(refund);
        _state.LogAction($"{owner.Name} sold a hotel on {property.Name} for ${refund}.");
        return true;
    }

    /// <summary>
    /// Mortgage a property (player gets half the purchase price in cash).
    /// </summary>
    public bool MortgageProperty(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || property.IsMortgaged || property.HouseCount > 0 || property.HasHotel)
            return false;

        property.IsMortgaged = true;
        owner.AddCash(property.MortgageValue);
        _state.LogAction($"{owner.Name} mortgaged {property.Name} for ${property.MortgageValue}.");
        return true;
    }

    /// <summary>
    /// Unmortgage a property. Player pays mortgage value plus 10% interest.
    /// </summary>
    public bool UnmortgageProperty(Property property)
    {
        var owner = _state.GetPlayerById(property.OwnerId!);
        if (owner == null || !property.IsMortgaged)
            return false;

        int cost = (int)(property.MortgageValue * 1.1);
        if (owner.DeductCash(cost))
        {
            property.IsMortgaged = false;
            _state.LogAction($"{owner.Name} unmortgaged {property.Name} for ${cost}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Draw a card from Chance or Community Chest and execute its effect.
    /// </summary>
    public void DrawAndExecuteCard(CardDeck deck)
    {
        var player = _state.GetCurrentPlayer();
        if (player == null)
            return;

        var card = _cardDeckManager.DrawCard(deck);
        _state.LogAction($"{player.Name} drew: {card.Title}");

        ExecuteCardEffect(player, card);

        // If card is kept by player, don't return to deck
        if (!card.IsKeptByPlayer)
        {
            _cardDeckManager.ReturnCardToBottom(card);
        }
    }

    /// <summary>
    /// Execute the effect of a card on a player.
    /// </summary>
    private void ExecuteCardEffect(Player player, Card card)
    {
        switch (card.Type)
        {
            case CardType.MoveToGo:
                player.Position = 0;
                player.AddCash(Constants.GameConstants.GoPassingBonus);
                _state.LogAction($"{player.Name} moved to Go and collected $200.");
                break;

            case CardType.MoveToJail:
                SendPlayerToJail(player);
                break;

            case CardType.MoveToJustVisiting:
                player.Position = 10; // Jail position, but just visiting
                _state.LogAction($"{player.Name} moved to Jail (just visiting).");
                break;

            case CardType.MoveToSpecificLocation:
                if (card.TargetPosition.HasValue)
                {
                    int target = card.TargetPosition.Value;
                    if (target < player.Position)
                    {
                        player.AddCash(Constants.GameConstants.GoPassingBonus);
                        _state.LogAction($"{player.Name} passed Go and collected $200.");
                    }

                    player.Position = target;
                    _state.LogAction($"{player.Name} moved to {_state.Board.GetProperty(target).Name}.");
                    HandleLandingOnSpace(player, target);
                }

                break;

            case CardType.MoveForward:
                if (card.MoveSpaces.HasValue)
                {
                    MovePlayer(card.MoveSpaces.Value);
                }

                break;

            case CardType.MoveBackward:
                if (card.MoveSpaces.HasValue)
                {
                    MovePlayer(card.MoveSpaces.Value);
                }

                break;

            case CardType.Advance:
                // Find nearest railroad or utility
                if (card.DeckType == CardDeck.Chance)
                {
                    AdvanceToNearestSpace(player, card.Description);
                }

                break;

            case CardType.CollectFromBank:
                if (card.Amount.HasValue)
                {
                    player.AddCash(card.Amount.Value);
                    _state.LogAction($"{player.Name} collected ${card.Amount.Value} from the bank.");
                }

                break;

            case CardType.PayBank:
                if (card.Amount.HasValue)
                {
                    if (player.DeductCash(card.Amount.Value))
                    {
                        _state.LogAction($"{player.Name} paid ${card.Amount.Value} to the bank.");
                    }
                    else
                    {
                        _state.LogAction($"{player.Name} couldn't afford ${card.Amount.Value}. Bankrupt!");
                        BankruptPlayer(player, creditor: null);
                    }
                }

                break;

            case CardType.PayEachPlayer:
                if (card.Amount.HasValue)
                {
                    PayEachPlayer(player, card.Amount.Value);
                }

                break;

            case CardType.CollectFromEachPlayer:
                if (card.Amount.HasValue)
                {
                    CollectFromEachPlayer(player, card.Amount.Value);
                }

                break;

            case CardType.PayForHouseRepairs:
                if (card.HouseRepairCost.HasValue && card.HotelRepairCost.HasValue)
                {
                    PayForRepairs(player, card.HouseRepairCost.Value, card.HotelRepairCost.Value);
                }

                break;

            case CardType.GetOutOfJailFree:
                player.KeptCards.Add(card);
                _state.LogAction($"{player.Name} kept the Get Out of Jail Free card.");
                break;
        }
    }

    /// <summary>
    /// Advance player to nearest railroad or utility.
    /// </summary>
    private void AdvanceToNearestSpace(Player player, string description)
    {
        int currentPos = player.Position;
        int targetPos = -1;

        if (description.Contains("Utility"))
        {
            // Find nearest utility: positions 12, 28
            int[] utilities = { 12, 28 };
            targetPos = utilities.FirstOrDefault(u => u > currentPos);
            if (targetPos == 0)
                targetPos = utilities[0];
        }
        else if (description.Contains("Railroad"))
        {
            // Find nearest railroad: positions 5, 15, 25, 35
            int[] railroads = { 5, 15, 25, 35 };
            targetPos = railroads.FirstOrDefault(r => r > currentPos);
            if (targetPos == 0)
                targetPos = railroads[0];
        }

        if (targetPos > currentPos)
        {
            MovePlayer(targetPos - currentPos);
        }
        else if (targetPos > 0)
        {
            // Wrapped around, pass Go
            player.AddCash(Constants.GameConstants.GoPassingBonus);
            MovePlayer((40 - currentPos) + targetPos);
        }
    }

    /// <summary>
    /// Player pays each other player a set amount. Bankruptcy is to the bank (no single creditor).
    /// </summary>
    private void PayEachPlayer(Player payer, int amount)
    {
        int totalOwed = amount * (_state.Players.Count(p => !p.IsBankrupt) - 1);

        if (payer.Cash >= totalOwed)
        {
            foreach (var otherPlayer in _state.Players.Where(p => p.Id != payer.Id && !p.IsBankrupt))
            {
                payer.DeductCash(amount);
                otherPlayer.AddCash(amount);
            }

            _state.LogAction($"{payer.Name} paid ${amount} to each other player.");
        }
        else
        {
            int available = payer.Cash;
            payer.DeductCash(available);
            _state.LogAction($"{payer.Name} couldn't afford to pay each player. {payer.Name} is bankrupt!");
            BankruptPlayer(payer, creditor: null);
        }
    }

    /// <summary>
    /// Player collects a set amount from each other player.
    /// Players who cannot pay are bankrupted with the collector as creditor.
    /// </summary>
    private void CollectFromEachPlayer(Player collector, int amount)
    {
        foreach (var otherPlayer in _state.Players.Where(p => p.Id != collector.Id && !p.IsBankrupt).ToList())
        {
            if (otherPlayer.DeductCash(amount))
            {
                collector.AddCash(amount);
            }
            else
            {
                // Other player goes bankrupt
                int available = otherPlayer.Cash;
                otherPlayer.DeductCash(available);
                collector.AddCash(available);
                _state.LogAction($"{otherPlayer.Name} couldn't pay {collector.Name} and is bankrupt!");
                BankruptPlayer(otherPlayer, creditor: collector);
            }
        }

        _state.LogAction($"{collector.Name} collected ${amount} from each other player.");
    }

    /// <summary>
    /// Player pays for repairs on their houses and hotels. Bankruptcy goes to the bank.
    /// </summary>
    private void PayForRepairs(Player player, int houseCost, int hotelCost)
    {
        int totalCost = 0;
        var ownedProperties = _state.Board.GetPropertiesByOwner(player.Id);

        foreach (var property in ownedProperties)
        {
            totalCost += property.HasHotel ? hotelCost : property.HouseCount * houseCost;
        }

        if (totalCost == 0)
        {
            _state.LogAction($"{player.Name} has no properties to repair.");
            return;
        }

        if (player.DeductCash(totalCost))
        {
            _state.LogAction(
                $"{player.Name} paid ${totalCost} for repairs ({houseCost} per house, {hotelCost} per hotel).");
        }
        else
        {
            _state.LogAction($"{player.Name} couldn't afford ${totalCost} in repairs. {player.Name} is bankrupt!");
            BankruptPlayer(player, creditor: null);
        }
    }

    /// <summary>
    /// Use a Get Out of Jail Free card from the player's hand to leave jail.
    /// </summary>
    public bool UseGetOutOfJailFreeCard(Player player)
    {
        var jailFreeCard = player.KeptCards.FirstOrDefault(c => c.Type == CardType.GetOutOfJailFree);
        if (jailFreeCard == null)
            return false;

        player.KeptCards.Remove(jailFreeCard);
        player.ReleaseFromJail();
        _cardDeckManager.ReturnCardToBottom(jailFreeCard);
        _state.LogAction($"{player.Name} used a Get Out of Jail Free card.");
        return true;
    }

    /// <summary>
    /// Propose a trade between two players.
    /// </summary>
    public TradeOffer? ProposeTrade(string fromPlayerId, string toPlayerId, TradeOffer offer)
    {
        var fromPlayer = _state.GetPlayerById(fromPlayerId);
        var toPlayer = _state.GetPlayerById(toPlayerId);

        if (fromPlayer == null || toPlayer == null)
            return null;

        if (fromPlayer.IsBankrupt || toPlayer.IsBankrupt)
            return null;

        // Validate offering player has the properties and cash they're offering
        if (!ValidateTradeAssets(fromPlayer, offer.OfferedPropertyIds, offer.OfferedCash, offer.OfferedCardIds))
            return null;

        // Validate requesting player has the properties and cash they're being asked for
        if (!ValidateTradeAssets(toPlayer, offer.RequestedPropertyIds, offer.RequestedCash, offer.RequestedCardIds))
            return null;

        // Check for duplicate pending trades from same player
        var existingTrade = _state.PendingTrades.FirstOrDefault(t =>
            t.FromPlayerId == fromPlayerId &&
            t.ToPlayerId == toPlayerId &&
            t.Status == TradeStatus.Pending);

        if (existingTrade != null)
        {
            _state.LogAction($"{fromPlayer.Name} already has a pending trade with {toPlayer.Name}.");
            return null;
        }

        offer.Id = Guid.NewGuid().ToString();
        offer.FromPlayerId = fromPlayerId;
        offer.ToPlayerId = toPlayerId;
        offer.CreatedAt = DateTime.UtcNow;
        offer.Status = TradeStatus.Pending;

        _state.PendingTrades.Add(offer);
        _state.LogAction($"{fromPlayer.Name} proposed a trade to {toPlayer.Name}.");

        return offer;
    }

    /// <summary>
    /// Accept a pending trade offer.
    /// </summary>
    public bool AcceptTrade(string tradeId, string acceptingPlayerId)
    {
        var trade = _state.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
        if (trade == null || trade.Status != TradeStatus.Pending)
            return false;

        // Only the receiving player can accept
        if (trade.ToPlayerId != acceptingPlayerId)
            return false;

        var fromPlayer = _state.GetPlayerById(trade.FromPlayerId);
        var toPlayer = _state.GetPlayerById(trade.ToPlayerId);

        if (fromPlayer == null || toPlayer == null)
            return false;

        // Execute the trade
        ExecuteTrade(fromPlayer, toPlayer, trade);

        trade.Status = TradeStatus.Accepted;
        trade.RespondedAt = DateTime.UtcNow;

        _state.LogAction($"{toPlayer.Name} accepted {fromPlayer.Name}'s trade.");
        return true;
    }

    /// <summary>
    /// Reject a pending trade offer.
    /// </summary>
    public bool RejectTrade(string tradeId, string rejectingPlayerId)
    {
        var trade = _state.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
        if (trade == null || trade.Status != TradeStatus.Pending)
            return false;

        // Only the receiving player can reject
        if (trade.ToPlayerId != rejectingPlayerId)
            return false;

        var fromPlayer = _state.GetPlayerById(trade.FromPlayerId);
        var toPlayer = _state.GetPlayerById(trade.ToPlayerId);

        trade.Status = TradeStatus.Rejected;
        trade.RespondedAt = DateTime.UtcNow;

        if (toPlayer != null && fromPlayer != null)
        {
            _state.LogAction($"{toPlayer.Name} rejected {fromPlayer.Name}'s trade.");
        }

        return true;
    }

    /// <summary>
    /// Cancel a trade offer (proposer only).
    /// </summary>
    public bool CancelTrade(string tradeId, string cancelingPlayerId)
    {
        var trade = _state.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
        if (trade == null || trade.Status != TradeStatus.Pending)
            return false;

        // Only the offering player can cancel
        if (trade.FromPlayerId != cancelingPlayerId)
            return false;

        var fromPlayer = _state.GetPlayerById(trade.FromPlayerId);

        trade.Status = TradeStatus.Cancelled;
        trade.RespondedAt = DateTime.UtcNow;

        if (fromPlayer != null)
        {
            _state.LogAction($"{fromPlayer.Name} cancelled the trade.");
        }

        return true;
    }

    /// <summary>
    /// Execute a trade between two players (transfer assets).
    /// </summary>
    private void ExecuteTrade(Player fromPlayer, Player toPlayer, TradeOffer trade)
    {
        if (!ValidateTradeAssets(fromPlayer, trade.OfferedPropertyIds, trade.OfferedCash, trade.OfferedCardIds))
        {
            _state.LogAction($"Trade failed: {fromPlayer.Name} no longer has the offered assets");
            return;
        }

        if (!ValidateTradeAssets(toPlayer, trade.RequestedPropertyIds, trade.RequestedCash, trade.RequestedCardIds))
        {
            _state.LogAction($"Trade failed: {toPlayer.Name} no longer has the requested assets");
            return;
        }

        if (trade.OfferedCash > 0)
        {
            fromPlayer.DeductCash(trade.OfferedCash);
            toPlayer.AddCash(trade.OfferedCash);
        }

        if (trade.RequestedCash > 0)
        {
            toPlayer.DeductCash(trade.RequestedCash);
            fromPlayer.AddCash(trade.RequestedCash);
        }

        foreach (var propertyId in trade.OfferedPropertyIds)
        {
            var property = _state.Board.GetProperty(propertyId);
            if (property.OwnerId == fromPlayer.Id)
            {
                property.OwnerId = toPlayer.Id;
            }
        }

        foreach (var propertyId in trade.RequestedPropertyIds)
        {
            var property = _state.Board.GetProperty(propertyId);
            if (property.OwnerId == toPlayer.Id)
            {
                property.OwnerId = fromPlayer.Id;
            }
        }

        foreach (var cardId in trade.OfferedCardIds)
        {
            var card = fromPlayer.KeptCards.FirstOrDefault(c => c.Id == cardId);
            if (card != null)
            {
                fromPlayer.KeptCards.Remove(card);
                toPlayer.KeptCards.Add(card);
            }
        }

        foreach (var cardId in trade.RequestedCardIds)
        {
            var card = toPlayer.KeptCards.FirstOrDefault(c => c.Id == cardId);
            if (card != null)
            {
                toPlayer.KeptCards.Remove(card);
                fromPlayer.KeptCards.Add(card);
            }
        }

        _state.LogAction($"Trade executed: {fromPlayer.Name} <-> {toPlayer.Name}");
    }

    /// <summary>
    /// Validate that a player has the assets they're offering/being asked for.
    /// </summary>
    private bool ValidateTradeAssets(Player player, List<int> propertyIds, int cash, List<string> cardIds)
    {
        // Check cash
        if (cash > player.Cash)
            return false;

        // Check properties
        var ownedPropertyIds = _state.Board.GetPropertiesByOwner(player.Id).Select(p => p.Id).ToList();
        foreach (var propId in propertyIds)
        {
            if (!ownedPropertyIds.Contains(propId))
                return false;
        }

        // Check cards
        var ownedCardIds = player.KeptCards.Select(c => c.Id).ToList();
        foreach (var cardId in cardIds)
        {
            if (!ownedCardIds.Contains(cardId))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Toggles the mortgage status of a property.
    /// </summary>
    public bool ToggleMortgage(int propertyId)
    {
        var property = _state.Board.GetProperty(propertyId);

        return property.IsMortgaged ? UnmortgageProperty(property) : MortgageProperty(property);
    }
}