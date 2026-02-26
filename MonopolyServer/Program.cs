using MonopolyServer.Game.Constants;
using MonopolyServer.Hubs;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer;

/// <summary>
/// Entry point for the MonopolyServer application. Configures services (SignalR, CORS, game services, logging)
/// and maps HTTP and SignalR endpoints before starting the web host.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container
        builder.Services.AddSignalR();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        builder.Services.AddSingleton<InputValidator>();

        builder.Services.AddSingleton<GameRoomManager>();
        builder.Services.AddSingleton<TradeService>();
        builder.Services.AddSingleton<LobbyService>();

        builder.Services.AddHostedService<GameCleanupService>();

        builder.Services.AddLogging(config =>
        {
            config.AddConsole();
            config.AddDebug();
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline
        app.UseRouting();

        // Enable CORS for SignalR
        app.UseCors("AllowAll");

        // Map SignalR hub
        app.MapHub<GameHub>("/game-hub");

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("Health");

        app.MapGet("/api/stats", (GameRoomManager roomManager) =>
        {
            var stats = roomManager.GetStats();
            return Results.Ok(stats);
        });

        app.MapGet("/api/games", (GameRoomManager roomManager) =>
        {
            var games = roomManager.GetAllGames()
                .Where(g => g.Status != MonopolyServer.Game.Models.Enums.GameStatus.Finished)
                .Select(g => new
                {
                    gameId = g.GameId,
                    status = g.Status.ToString(),
                    playerCount = g.Players.Count,
                    maxPlayers = GameConstants.MaxPlayers,
                    createdAt = g.CreatedAt
                })
                .ToList();

            return Results.Ok(games);
        });

        app.Run();
    }
}