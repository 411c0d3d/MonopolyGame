namespace MonopolyServer.Game.Constants;

/// <summary>
/// Central configuration and constants for Monopoly game rules.
/// Extracted magic numbers for easy tuning and maintenance.
/// </summary>
public static class GameConstants
{
    // Player Configuration
    public const int MinPlayers = 2;
    public const int MaxPlayers = 8;
    public const int StartingCash = 1500;
    public const int StartingPosition = 0;

    // Dice Configuration
    public const int MaxConsecutiveDoubles = 3;

    // Jail Configuration
    public const int JailPosition = 10;
    public const int JailBailCost = 50;
    public const int MaxJailTurns = 3;

    // Property Development
    public const int MaxHousesPerProperty = 4;
    public const int HouseSellFactor = 2; // Sell for 1/2 price
    public const int HotelSellFactor = 2; // Sell for 1/2 price

    // Board Configuration
    public const int BoardSize = 40;
    public const int GoPosition = 0;
    public const int GoPassingBonus = 200;
    public const int IncomeTax = 200;
    public const int LuxuryTax = 100;

    // Game Room Configuration
    public const int GameIdLength = 8;
    public const string GameIdChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    // Validation Limits
    public const int MaxPlayerNameLength = 50;
    public const int MinPlayerNameLength = 1;
    public const int MaxTradeProperties = 10;
    public const int MaxTradeCash = 100000;

    // Persistence Configuration
    public const string GamesDirectoryName = "games";
    public const string DataDirectoryName = "data";
    public const string GameFileExtension = ".json";

    // Game Log Configuration
    public const int MaxGameLogEntries = 20;

    // Cleanup Configuration
    public const int PlayerDisconnectTimeoutMinutes = 10;
    public const int AbandonedGameCleanupHours = 1;
    public const int FinishedGameCleanupDays = 7;
    public const int CleanupIntervalMinutes = 5;
}