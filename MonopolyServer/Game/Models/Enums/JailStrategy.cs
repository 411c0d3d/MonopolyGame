namespace MonopolyServer.Game.Models.Enums;

/// <summary>
/// Possible strategies a bot can use when stuck in jail.
/// </summary>
public enum JailStrategy
{
    /// <summary>Use a held Get Out of Jail Free card.</summary>
    UseCard,

    /// <summary>Pay the $50 bail immediately.</summary>
    PayBail,

    /// <summary>Attempt to roll doubles; accept forced bail on the third turn.</summary>
    Roll
}