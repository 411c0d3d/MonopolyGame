using MonopolyServer.Game.Models.Enums;

namespace MonopolyServer.DTOs;

/// <summary>
/// Data transfer object that represents aggregated health and diagnostics for the server's game rooms.
/// Contains counters for total games, counts per game status, and the total number of players.
/// </summary>
public sealed class ServerHealthStatsDto
{
    /// <summary>
    /// Initializes a new instance of <see cref="ServerHealthStatsDto"/>.
    /// </summary>
    /// <param name="totalGames">Total number of games currently managed.</param>
    /// <param name="gamesInProgress">Number of games with status <see cref="GameStatus.InProgress"/>.</param>
    /// <param name="gamesWaiting">Number of games with status <see cref="GameStatus.Waiting"/>.</param>
    /// <param name="gamesPaused">Number of games with status <see cref="GameStatus.Paused"/>.</param>
    /// <param name="gamesFinished">Number of games with status <see cref="GameStatus.Finished"/>.</param>
    /// <param name="totalPlayers">Sum of player counts across all games.</param>
    public ServerHealthStatsDto(int totalGames, int gamesInProgress, int gamesWaiting, int gamesPaused, int gamesFinished, int totalPlayers)
    {
        TotalGames = totalGames;
        GamesInProgress = gamesInProgress;
        GamesWaiting = gamesWaiting;
        GamesPaused = gamesPaused;
        GamesFinished = gamesFinished;
        TotalPlayers = totalPlayers;
    }

    /// <summary>
    /// Total number of games currently managed.
    /// </summary>
    public int TotalGames { get; init; }

    /// <summary>
    /// Number of games currently in progress.
    /// </summary>
    public int GamesInProgress { get; init; }

    /// <summary>
    /// Number of games waiting for players.
    /// </summary>
    public int GamesWaiting { get; init; }

    /// <summary>
    /// Number of paused games.
    /// </summary>
    public int GamesPaused { get; init; }

    /// <summary>
    /// Number of finished games.
    /// </summary>
    public int GamesFinished { get; init; }

    /// <summary>
    /// Total number of players across all games.
    /// </summary>
    public int TotalPlayers { get; init; }
}