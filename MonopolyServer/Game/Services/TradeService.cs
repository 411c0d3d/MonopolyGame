using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Service layer for trade operations.
/// Decoupled from SignalR so it can be tested independently.
/// </summary>
public class TradeService
{
    private readonly GameRoomManager _roomManager;
    private readonly ILogger<TradeService> _logger;

    /// <summary>
    /// Constructor with dependency injection of GameRoomManager for accessing game state and ILogger for logging trade actions.
    /// </summary>
    public TradeService(GameRoomManager roomManager, ILogger<TradeService> logger)
    {
        _roomManager = roomManager;
        _logger = logger;
    }

    /// <summary>
    /// Proposes a trade between two players.
    /// Any existing pending offer from the same sender is cancelled first,
    /// ensuring only one active offer per sender exists at a time.
    /// </summary>
    public TradeOffer? ProposeTrade(string gameId, string fromPlayerId, string toPlayerId, TradeOffer tradeOffer)
    {
        tradeOffer.FromPlayerId = fromPlayerId;
        tradeOffer.ToPlayerId = toPlayerId;

        TradeOffer? result = null;

        _roomManager.MutateGame(gameId, (game, engine) =>
        {
            // Cancel any prior pending offer from this sender before proposing a new one.
            var prior = game.PendingTrades.FirstOrDefault(t =>
                t.FromPlayerId == fromPlayerId && t.Status == TradeStatus.Pending);

            if (prior != null)
            {
                engine?.CancelTrade(prior.Id, fromPlayerId);
                _logger.LogInformation("Prior trade {TradeId} from {Player} replaced by new offer",
                    prior.Id,
                    game.GetPlayerById(fromPlayerId)?.Name);
            }

            result = engine?.ProposeTrade(tradeOffer);

            if (result != null)
            {
                _logger.LogInformation("Trade proposed: {From} -> {To}",
                    game.GetPlayerById(fromPlayerId)?.Name,
                    game.GetPlayerById(toPlayerId)?.Name);
            }
        });

        return result;
    }

    /// <summary>
    /// Accept a pending trade.
    /// </summary>
    public bool AcceptTrade(string gameId, string tradeId, string acceptingPlayerId)
    {
        bool success = false;

        _roomManager.MutateGame(gameId, (game, engine) =>
        {
            success = engine?.AcceptTrade(tradeId, acceptingPlayerId) ?? false;

            if (success)
            {
                var trade = game.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
                _logger.LogInformation("Trade accepted: {Proposing} <-> {Accepting}",
                    trade != null ? game.GetPlayerById(trade.FromPlayerId)?.Name : "unknown",
                    game.GetPlayerById(acceptingPlayerId)?.Name);
            }
        });

        return success;
    }

    /// <summary>
    /// Reject a pending trade.
    /// </summary>
    public bool RejectTrade(string gameId, string tradeId, string rejectingPlayerId)
    {
        bool success = false;

        _roomManager.MutateGame(gameId, (game, engine) =>
        {
            var trade = game.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
            success = engine?.RejectTrade(tradeId, rejectingPlayerId) ?? false;

            if (success)
            {
                _logger.LogInformation("Trade rejected: {Proposing} <- {Rejecting}",
                    trade != null ? game.GetPlayerById(trade.FromPlayerId)?.Name : "unknown",
                    game.GetPlayerById(rejectingPlayerId)?.Name);
            }
        });

        return success;
    }

    /// <summary>
    /// Cancel a pending trade.
    /// </summary>
    public bool CancelTrade(string gameId, string tradeId, string cancelingPlayerId)
    {
        bool success = false;

        _roomManager.MutateGame(gameId, (game, engine) =>
        {
            success = engine?.CancelTrade(tradeId, cancelingPlayerId) ?? false;

            if (success)
            {
                _logger.LogInformation("Trade cancelled by {Player}",
                    game.GetPlayerById(cancelingPlayerId)?.Name);
            }
        });

        return success;
    }

    /// <summary>
    /// Get a trade by ID.
    /// </summary>
    public TradeOffer? GetTrade(string gameId, string tradeId)
    {
        var game = _roomManager.GetGame(gameId);
        return game?.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
    }

    /// <summary>
    /// Get all pending trades for a game.
    /// </summary>
    public List<TradeOffer> GetPendingTrades(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        return game?.PendingTrades.Where(t => t.Status == TradeStatus.Pending).ToList() ?? [];
    }

    /// <summary>
    /// Get trades pending for a specific player (trades waiting for their response).
    /// </summary>
    public List<TradeOffer> GetPendingTradesForPlayer(string gameId, string playerId)
    {
        var game = _roomManager.GetGame(gameId);
        return game?.PendingTrades
            .Where(t => t.ToPlayerId == playerId && t.Status == TradeStatus.Pending)
            .ToList() ?? [];
    }
}