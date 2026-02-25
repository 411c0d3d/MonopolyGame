namespace MonopolyServer.Game.Models;

/// <summary>
/// Represents a player in the Monopoly game, tracking identity, finances, board position, jail status, kept cards, and per-turn state.
/// </summary>
public class Player
{
    /// <summary>
    /// Unique identifier for the player.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name of the player.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Current cash balance for the player (in dollars).
    /// </summary>
    public int Cash { get; set; }

    /// <summary>
    /// Current board position (0-39).
    /// </summary>
    public int Position { get; set; } // 0-39 on board

    /// <summary>
    /// Indicates whether the player is currently in jail.
    /// </summary>
    public bool IsInJail { get; set; }

    /// <summary>
    /// Number of turns remaining in jail before the player is released or must take further action.
    /// </summary>
    public int JailTurnsRemaining { get; set; }

    /// <summary>
    /// Collection of cards held by the player (for example, "Get Out of Jail Free" cards).
    /// </summary>
    public List<Card> KeptCards { get; set; } // Cards held by player (Get Out of Jail Free)

    /// <summary>
    /// Indicates whether the player has gone bankrupt.
    /// </summary>
    public bool IsBankrupt { get; set; }

    /// <summary>
    /// UTC timestamp when the player joined the game.
    /// </summary>
    public DateTime JoinedAt { get; set; }

    // Game state
    /// <summary>
    /// True when it is this player's turn.
    /// </summary>
    public bool IsCurrentPlayer { get; set; }

    /// <summary>
    /// True when the player has rolled dice this turn.
    /// </summary>
    public bool HasRolledDice { get; set; }

    /// <summary>
    /// Value of the last dice roll for the player.
    /// </summary>
    public int LastDiceRoll { get; set; }

    /// <summary>
    /// Initializes a new Player with the specified id, name, and optional starting cash.
    /// </summary>
    /// <param name="id">Unique player identifier.</param>
    /// <param name="name">Player display name.</param>
    /// <param name="startingCash">Initial cash amount (default 1500).</param>
    public Player(string id, string name, int startingCash = 1500)
    {
        Id = id;
        Name = name;
        Cash = startingCash;
        Position = 0; // Start at Go
        IsInJail = false;
        JailTurnsRemaining = 0;
        KeptCards = new List<Card>();
        IsBankrupt = false;
        JoinedAt = DateTime.UtcNow;
        IsCurrentPlayer = false;
        HasRolledDice = false;
        LastDiceRoll = 0;
    }

    /// <summary>
    /// Move the player to a new board position. Awards $200 when passing GO.
    /// </summary>
    /// <param name="newPosition">Target board position (may exceed 39 to indicate passing GO).</param>
    public void MoveTo(int newPosition)
    {
        if (newPosition >= 40)
        {
            // Passed Go, collect $200
            Cash += 200;
            Position = newPosition % 40;
        }
        else
        {
            Position = newPosition;
        }
    }

    /// <summary>
    /// Add cash to the player's balance.
    /// </summary>
    /// <param name="amount">Amount to add (positive value).</param>
    public void AddCash(int amount)
    {
        Cash += amount;
    }

    /// <summary>
    /// Attempt to deduct cash from the player's balance.
    /// </summary>
    /// <param name="amount">Amount to deduct.</param>
    /// <returns>True if the deduction succeeded; false if insufficient funds.</returns>
    public bool DeductCash(int amount)
    {
        if (Cash >= amount)
        {
            Cash -= amount;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Send the player to jail and reset jail-related counters.
    /// </summary>
    public void SendToJail()
    {
        IsInJail = true;
        JailTurnsRemaining = 3;
        Position = 10; // Jail position
    }

    /// <summary>
    /// Release the player from jail and clear jail counters.
    /// </summary>
    public void ReleaseFromJail()
    {
        IsInJail = false;
        JailTurnsRemaining = 0;
    }

    /// <summary>
    /// Mark the player as bankrupt and zero their cash.
    /// </summary>
    public void Bankrupt()
    {
        IsBankrupt = true;
        Cash = 0;
    }
}