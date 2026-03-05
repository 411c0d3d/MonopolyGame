using Microsoft.Extensions.Options;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Data.Repositories;

/// <summary>
/// DI registration extensions for persistence and game services.
///
/// Call order in Program.cs:
///   builder.Services.AddGamePersistence(builder.Configuration);
///   builder.Services.AddGameServices();
///   ...
///   await app.InitializeGameManagerAsync();
///   await app.RunAsync();
///
/// InitializeGameManagerAsync MUST be awaited before app.RunAsync() so that
/// GameRoomManager is fully loaded before GameCleanupService.ExecuteAsync fires.
/// </summary>
public static class PersistenceServiceExtensions
{
    /// <summary>
    /// Registers IGameRepository — either FileGameRepository or CosmosGameRepository
    /// depending on PersistenceSettings:UseDatabase in configuration.
    /// </summary>
    public static IServiceCollection AddGamePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useDatabase = configuration.GetValue<bool>("PersistenceSettings:UseDatabase");

        if (useDatabase)
        {
            services.Configure<CosmosSettings>(configuration.GetSection(CosmosSettings.SectionName));

            services.AddSingleton<IGameRepository>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<CosmosSettings>>().Value;
                var logger = sp.GetRequiredService<ILogger<CosmosGameRepository>>();
                var repo = new CosmosGameRepository(settings, logger);
                repo.InitializeAsync().GetAwaiter().GetResult();
                return repo;
            });
        }
        else
        {
            services.AddSingleton<IGameRepository, FileGameRepository>();
        }

        return services;
    }

    /// <summary>
    /// Registers GameRoomManager and GameCleanupService.
    /// Must be called after AddGamePersistence so IGameRepository is available.
    /// GameCleanupService is registered as a hosted service and will only start
    /// cleanly once InitializeGameManagerAsync has been awaited in Program.cs.
    /// </summary>
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        services.AddSingleton<GameRoomManager>();
        services.AddHostedService<GameCleanupService>();
        return services;
    }

    /// <summary>
    /// Awaits GameRoomManager.InitializeAsync() before the host begins serving requests.
    /// This guarantees GameCleanupService never runs against an empty in-memory store.
    /// Call this after app.Build() and before app.RunAsync().
    /// </summary>
    public static async Task InitializeGameManagerAsync(this WebApplication app)
    {
        var manager = app.Services.GetRequiredService<GameRoomManager>();
        await manager.InitializeAsync();
    }
}