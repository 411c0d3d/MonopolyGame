using MonopolyServer.DTOs;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Infrastructure;

/// <summary>Maps GameState domain objects to broadcast DTOs. Shared by GameHub and TurnTimerService.</summary>
public static class GameStateMapper
{
    /// <summary>Maps a GameState to its full DTO for client broadcast.</summary>
    public static GameStateDto ToDto(GameState game) => new()
    {
        GameId = game.GameId,
        HostId = game.HostId,
        Status = game.Status.ToString(),
        Turn = game.Turn,
        CurrentPlayerIndex = game.CurrentPlayerIndex,
        CurrentPlayer = game.GetCurrentPlayer() is { } cp ? ToPlayerDto(cp) : null,
        Players = game.Players.Select(ToPlayerDto).ToList(),
        Board = game.Board.Spaces.Select(s => new PropertyDto
        {
            Id = s.Id,
            Name = s.Name,
            Type = s.Type.ToString(),
            ColorGroup = s.ColorGroup,
            OwnerId = s.OwnerId,
            OwnerName = s.OwnerId != null ? game.GetPlayerById(s.OwnerId)?.Name : null,
            PurchasePrice = s.PurchasePrice,
            MortgageValue = s.MortgageValue,
            HouseCost = s.HouseCost,
            HotelCost = s.HotelCost,
            RentValues = s.RentValues,
            HouseCount = s.HouseCount,
            HasHotel = s.HasHotel,
            IsMortgaged = s.IsMortgaged
        }).ToList(),
        EventLog = game.EventLog.TakeLast(10).ToList(),
        CurrentTurnStartedAt = game.CurrentTurnStartedAt,
        FinishedAt = game.FinishedAt,
        CreatedAt = game.CreatedAt,
        StartedAt = game.StartedAt,
        EndedAt = game.EndedAt,
        LastDiceRoll = game.LastDiceRoll,
        DoubleRolled = game.DoubleRolled,
        PendingTrades = game.PendingTrades,
        WinnerId = game.WinnerId
    };

    /// <summary>Maps a Player to its DTO.</summary>
    public static PlayerDto ToPlayerDto(Player p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Cash = p.Cash,
        Position = p.Position,
        IsInJail = p.IsInJail,
        JailTurnsRemaining = p.JailTurnsRemaining,
        IsBankrupt = p.IsBankrupt,
        KeptCardCount = p.KeptCards.Count,
        IsConnected = p.IsConnected,
        DisconnectedAt = p.DisconnectedAt,
        JoinedAt = p.JoinedAt,
        IsCurrentPlayer = p.IsCurrentPlayer,
        HasRolledDice = p.HasRolledDice,
        LastDiceRoll = p.LastDiceRoll,
        ConsecutiveDoubles = p.ConsecutiveDoubles
    };
}