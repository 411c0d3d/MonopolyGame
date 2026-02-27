using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Hubs;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add SignalR and CORS for the external client
        builder.Services.AddSignalR();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.WithOrigins("http://localhost:5299") // Ensure this matches your Client's URL
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        // Register Game Services
        builder.Services.AddSingleton<InputValidator>();
        builder.Services.AddSingleton<GameRoomManager>();
        builder.Services.AddSingleton<TradeService>();
        builder.Services.AddSingleton<LobbyService>();

        // Register Background Workers
        builder.Services.AddHostedService<GameCleanupService>();
        builder.Services.AddHostedService<TurnTimerService>();

        builder.Services.AddLogging(config =>
        {
            config.AddConsole();
            config.AddDebug();
        });

        var app = builder.Build();

        // Server-only Middleware
        app.UseRouting();
        app.UseCors("AllowAll");

        // Hub Endpoints
        app.MapHub<GameHub>("/game-hub");
        app.MapHub<AdminHub>("/admin-hub");

        // API Endpoints
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/games",
            (GameRoomManager rm) =>
            {
                return Results.Ok(rm.GetAllGames().Where(g => g.Status != GameStatus.Finished));
            });

        // Fixed: Added POST endpoint to solve your 405 error
        app.MapPost("/api/games", (GameRoomManager rm) =>
        {
            var gameId = rm.CreateGame();
            return Results.Ok(new { gameId });
        });

        app.Run();
    }
}