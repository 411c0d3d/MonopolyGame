using MonopolyServer.DTOs;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;
using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.Game.Services;

/// <summary>
/// Service for lobby management and game discovery.
/// Provides caching and broadcasting of available games.
/// </summary>
public class LobbyService
{
    private readonly GameRoomManager _roomManager;

    /// <summary>
    /// Constructor with dependency injection of GameRoomManager.
    /// </summary>
    public LobbyService(GameRoomManager roomManager)
    {
        _roomManager = roomManager;
    }

    /// <summary>
    /// Get all available (Waiting) games ordered by creation time.
    /// </summary>
    public List<GameRoomInfo> GetAvailableGames()
    {
        return _roomManager.GetAllGames()
            .Where(g => g.Status == GameStatus.Waiting)
            .OrderByDescending(g => g.CreatedAt)
            .Select(MapToGameRoomInfo)
            .ToList();
    }

    /// <summary>
    /// Get lobby info for a specific waiting game.
    /// </summary>
    public GameRoomInfo? GetGameLobby(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Waiting)
        {
            return null;
        }

        return MapToGameRoomInfo(game);
    }

    /// <summary>
    /// Get games a user created or is participating in.
    /// </summary>
    public List<GameRoomInfo> GetUserGames(string userId)
    {
        return _roomManager.GetAllGames()
            .Where(g => g.HostId == userId || g.Players.Any(p => p.Id == userId))
            .Select(MapToGameRoomInfo)
            .ToList();
    }

    /// <summary>
    /// Maps a GameState to its lobby-facing DTO.
    /// </summary>
    private static GameRoomInfo MapToGameRoomInfo(GameState game)
    {
        var hostPlayer = game.Players.FirstOrDefault(p => p.Id == game.HostId);

        return new GameRoomInfo
        {
            GameId = game.GameId,
            HostId = game.HostId ?? string.Empty,
            HostName = hostPlayer?.Name ?? "Unknown",
            PlayerCount = game.Players.Count,
            MaxPlayers = GameConstants.MaxPlayers,
            Status = game.Status.ToString(),
            CreatedAt = game.CreatedAt,
            Players = game.Players.Select(p => new PlayerLobbyInfo
            {
                Id = p.Id,
                Name = p.Name,
                IsHost = p.Id == game.HostId,
                JoinedAt = p.JoinedAt
            }).ToList()
        };
    }
}