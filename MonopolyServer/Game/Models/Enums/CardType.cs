namespace MonopolyServer.Game.Models.Enums;

/// <summary>
/// Types of card effects that can be applied to a player.
/// </summary>
public enum CardType
{
    // Movement
    MoveToGo,
    MoveToJail,
    MoveToJustVisiting,
    MoveForward,
    MoveBackward,
    MoveToSpecificLocation,

    // Financial
    PayBank,
    CollectFromBank,
    PayEachPlayer,
    CollectFromEachPlayer,
    PayForHouseRepairs,

    // Jail
    GetOutOfJailFree,

    // Other
    Advance
}