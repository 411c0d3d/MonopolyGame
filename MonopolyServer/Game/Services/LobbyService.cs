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
    /// Constructor with dependency injection of GameRoomManager for accessing game state.
    /// </summary>
    /// <param name="roomManager"></param>
    public LobbyService(GameRoomManager roomManager)
    {
        _roomManager = roomManager;
    }

    /// <summary>
    /// Get all available (Waiting) games.
    /// </summary>
    public List<GameRoomInfo> GetAvailableGames()
    {
        var games = _roomManager.GetAllGames()
            .Where(g => g.Status == GameStatus.Waiting)
            .OrderByDescending(g => g.CreatedAt)
            .Select(game => MapToGameRoomInfo(game))
            .ToList();

        return games;
    }

    /// <summary>
    /// Get lobby info for a specific game.
    /// </summary>
    public GameRoomInfo? GetGameLobby(string gameId)
    {
        var game = _roomManager.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Waiting)
            return null;

        return MapToGameRoomInfo(game);
    }

    /// <summary>
    /// Get games a user created or is in.
    /// </summary>
    public List<GameRoomInfo> GetUserGames(string userId)
    {
        var games = _roomManager.GetAllGames()
            .Where(g => g.HostId == userId || g.Players.Any(p => p.Id == userId))
            .Select(g => MapToGameRoomInfo(g))
            .ToList();

        return games;
    }

    /// <summary>
    /// Get game room info with player details.
    /// </summary>
    private GameRoomInfo MapToGameRoomInfo(GameState game)
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