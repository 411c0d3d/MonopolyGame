using MonopolyServer.Hubs;
using MonopolyServer.Game.Services;

namespace MonopolyServer.Api;

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

        // Register game services (singleton so games persist across requests)
        builder.Services.AddSingleton<GameRoomManager>();
        builder.Services.AddSingleton<TradeService>();

        // Add logging
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

        // Health check endpoint
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        // Diagnostics endpoint
        app.MapGet("/api/games/stats", (GameRoomManager roomManager) =>
            Results.Ok(roomManager.GetStats())
        );

        app.MapGet("/api/games", (GameRoomManager roomManager) =>
            Results.Ok(roomManager.GetAllGames().Select(g => new
            {
                gameId = g.GameId,
                status = g.Status.ToString(),
                playerCount = g.Players.Count,
                hostId = g.HostId
            }))
        );

        app.Run();
    }
}