using MonopolyServer.Bot;
using MonopolyServer.Data.Repositories.GameStorage;
using MonopolyServer.Data.Repositories.UserAuth;
using MonopolyServer.Game.Models.Enums;
using MonopolyServer.Game.Services;
using MonopolyServer.Hubs;
using MonopolyServer.Infrastructure;
using MonopolyServer.Infrastructure.Budget;

namespace MonopolyServer;

/// <summary>
/// Main entry point for the Monopoly Server application. Configures services, middleware, and endpoints.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add SignalR and CORS for the external client
        builder.Services.AddSignalR();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.WithOrigins("http://localhost:5300")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        // Persistence — registers IGameRepository (file or Cosmos based on appsettings)
        builder.Services.AddGamePersistence(builder.Configuration);

        // Auth — Entra External ID JWT validation, role-based authorization, claims enrichment
        builder.Services.AddGameAuth(builder.Configuration);

        // Budget guard — free-tier consumption tracking and enforcement
        builder.Services.AddBudgetGuard(builder.Configuration);

        // Game services — registers GameRoomManager and GameCleanupService
        builder.Services.AddGameServices();

        builder.Services.AddSingleton<InputValidator>();
        builder.Services.AddSingleton<TradeService>();
        builder.Services.AddSingleton<LobbyService>();
        builder.Services.AddSingleton<BotDecisionEngine>();
        builder.Services.AddSingleton<BotTurnOrchestrator>();

        // Register Background Workers
        builder.Services.AddHostedService<TurnTimerService>();

        builder.Services.AddLogging(config =>
        {
            config.AddConsole();
            config.AddDebug();
        });

        var app = builder.Build();

        // Server-only Middleware
        app.UseMiddleware<BudgetMiddleware>();
        app.UseRouting();
        app.UseCors("AllowAll");

        // Auth middleware must be between routing and endpoints
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHub<GameHub>("/game-hub");
        app.MapHub<AdminHub>("/admin-hub");

        // API Endpoints
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/games",
            (GameRoomManager rm) =>
            {
                return Results.Ok(rm.GetAllGames().Where(g => g.Status != GameStatus.Finished));
            });

        // Added POST endpoint to solve your 405 error
        app.MapPost("/api/games", (GameRoomManager rm) =>
        {
            var gameId = rm.CreateGame();
            return Results.Ok(new { gameId });
        });

        // Must complete before RunAsync — guarantees GameRoomManager is fully loaded
        // before GameCleanupService.ExecuteAsync fires on startup.
        await app.InitializeBudgetContainerAsync();
        await app.InitializeGameManagerAsync();

        await app.RunAsync();
    }
}