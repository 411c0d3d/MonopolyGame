using System.Text.RegularExpressions;
using MonopolyServer.Game.Constants;

namespace MonopolyServer.Infrastructure;

/// <summary>
/// Centralized input validation service to prevent injection attacks,
/// crashes from malformed data, and enforce business rules.
/// </summary>
public class InputValidator
{
    private static readonly Regex _alphanumericRegex = new Regex(@"^[a-zA-Z0-9\s\-_]+$", RegexOptions.Compiled);
    private static readonly Regex _gameIdRegex = new Regex(@"^[A-Z0-9]{8}$", RegexOptions.Compiled);
    
    /// <summary>
    /// Validate player name meets requirements.
    /// </summary>
    public (bool IsValid, string? Error) ValidatePlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return (false, "Player name cannot be empty");
        }

        if (playerName.Length < GameConstants.MinPlayerNameLength)
        {
            return (false, $"Player name must be at least {GameConstants.MinPlayerNameLength} character");
        }

        if (playerName.Length > GameConstants.MaxPlayerNameLength)
        {
            return (false, $"Player name cannot exceed {GameConstants.MaxPlayerNameLength} characters");
        }

        if (!_alphanumericRegex.IsMatch(playerName))
        {
            return (false, "Player name can only contain letters, numbers, spaces, hyphens, and underscores");
        }

        return (true, null);
    }

    /// <summary>
    /// Validate game ID format.
    /// </summary>
    public (bool IsValid, string? Error) ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return (false, "Game ID cannot be empty");
        }

        if (!_gameIdRegex.IsMatch(gameId))
        {
            return (false, "Invalid game ID format");
        }

        return (true, null);
    }

    /// <summary>
    /// Validate property ID is within valid board range.
    /// </summary>
    public (bool IsValid, string? Error) ValidatePropertyId(int propertyId)
    {
        if (propertyId < 0 || propertyId >= GameConstants.BoardSize)
        {
            return (false, $"Property ID must be between 0 and {GameConstants.BoardSize - 1}");
        }

        return (true, null);
    }

    /// <summary>
    /// Validate trade cash amount.
    /// </summary>
    public (bool IsValid, string? Error) ValidateTradeCash(int amount)
    {
        if (amount < 0)
        {
            return (false, "Trade cash cannot be negative");
        }

        if (amount > GameConstants.MaxTradeCash)
        {
            return (false, $"Trade cash cannot exceed ${GameConstants.MaxTradeCash}");
        }

        return (true, null);
    }

    /// <summary>
    /// Validate trade property list size.
    /// </summary>
    public (bool IsValid, string? Error) ValidateTradeProperties(List<int> propertyIds)
    {
        if (propertyIds.Count > GameConstants.MaxTradeProperties)
        {
            return (false, $"Cannot trade more than {GameConstants.MaxTradeProperties} properties at once");
        }

        foreach (var id in propertyIds)
        {
            var (isValid, error) = ValidatePropertyId(id);
            if (!isValid)
            {
                return (false, error);
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Sanitize player name by trimming and removing extra whitespace.
    /// </summary>
    public string SanitizePlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return string.Empty;
        }

        // Trim and collapse multiple spaces
        return Regex.Replace(playerName.Trim(), @"\s+", " ");
    }
}