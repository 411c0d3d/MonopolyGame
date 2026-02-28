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
    /// <param name="roomManager"></param>
    /// <param name="logger"></param>
    public TradeService(GameRoomManager roomManager, ILogger<TradeService> logger)
    {
        _roomManager = roomManager;
        _logger = logger;
    }

    /// <summary>
    /// Propose a trade between two players.
    /// </summary>
    public TradeOffer? ProposeTrade(string gameId, string fromPlayerId, string toPlayerId, TradeOffer tradeOffer)
    {
        var game = _roomManager.GetGame(gameId);
        var engine = _roomManager.GetGameEngine(gameId);

        if (game == null || engine == null)
        {
            return null;
        }

        var result = engine.ProposeTrade(tradeOffer);
        if (result != null)
        {
            var fromPlayer = game.GetPlayerById(fromPlayerId);
            var toPlayer = game.GetPlayerById(toPlayerId);
            _logger.LogInformation("Trade proposed: {From} -> {To}", fromPlayer?.Name, toPlayer?.Name);
        }

        return result;
    }

    /// <summary>
    /// Accept a pending trade.
    /// </summary>
    public bool AcceptTrade(string gameId, string tradeId, string acceptingPlayerId)
    {
        var game = _roomManager.GetGame(gameId);
        var engine = _roomManager.GetGameEngine(gameId);

        if (game == null || engine == null)
            return false;

        var success = engine.AcceptTrade(tradeId, acceptingPlayerId);
        if (success)
        {
            var trade = game.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
            if (trade != null)
            {
                var acceptingPlayer = game.GetPlayerById(acceptingPlayerId);
                var proposingPlayer = game.GetPlayerById(trade.FromPlayerId);
                _logger.LogInformation($"Trade accepted: {proposingPlayer?.Name} <-> {acceptingPlayer?.Name}");
            }
        }

        return success;
    }

    /// <summary>
    /// Reject a pending trade.
    /// </summary>
    public bool RejectTrade(string gameId, string tradeId, string rejectingPlayerId)
    {
        var game = _roomManager.GetGame(gameId);
        var engine = _roomManager.GetGameEngine(gameId);

        if (game == null || engine == null)
            return false;

        var success = engine.RejectTrade(tradeId, rejectingPlayerId);
        if (success)
        {
            var trade = game.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
            if (trade != null)
            {
                var rejectingPlayer = game.GetPlayerById(rejectingPlayerId);
                var proposingPlayer = game.GetPlayerById(trade.FromPlayerId);
                _logger.LogInformation($"Trade rejected: {proposingPlayer?.Name} <- {rejectingPlayer?.Name}");
            }
        }

        return success;
    }

    /// <summary>
    /// Cancel a pending trade.
    /// </summary>
    public bool CancelTrade(string gameId, string tradeId, string cancelingPlayerId)
    {
        var game = _roomManager.GetGame(gameId);
        var engine = _roomManager.GetGameEngine(gameId);

        if (game == null || engine == null)
            return false;

        var success = engine.CancelTrade(tradeId, cancelingPlayerId);
        if (success)
        {
            var trade = game.PendingTrades.FirstOrDefault(t => t.Id == tradeId);
            if (trade != null)
            {
                var cancelingPlayer = game.GetPlayerById(cancelingPlayerId);
                _logger.LogInformation($"Trade cancelled: {cancelingPlayer?.Name}");
            }
        }

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
        return game?.PendingTrades.Where(t => t.Status == TradeStatus.Pending).ToList() ?? new();
    }

    /// <summary>
    /// Get trades pending for a specific player (trades waiting for their response).
    /// </summary>
    public List<TradeOffer> GetPendingTradesForPlayer(string gameId, string playerId)
    {
        var game = _roomManager.GetGame(gameId);
        return game?.PendingTrades
            .Where(t => t.ToPlayerId == playerId && t.Status == TradeStatus.Pending)
            .ToList() ?? new();
    }
}