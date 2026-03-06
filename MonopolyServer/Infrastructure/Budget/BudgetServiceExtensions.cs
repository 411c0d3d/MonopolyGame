using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// DI registration extensions for the budget guard feature.
/// Must be called after AddGamePersistence so CosmosClient is already registered.
/// No-ops cleanly when UseDatabase is false — safe to call unconditionally from Program.cs.
/// </summary>
public static class BudgetServiceExtensions
{
    public const string BudgetContainerId = "budget";

    /// <summary>
    /// Registers BudgetGuardService as a hosted singleton and wires its Cosmos container.
    /// Skips registration entirely when PersistenceSettings:UseDatabase is false.
    /// </summary>
    public static IServiceCollection AddBudgetGuard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useDatabase = configuration.GetValue<bool>("PersistenceSettings:UseDatabase");
        if (!useDatabase) { return services; }

        services.AddSingleton<BudgetGuardService>(sp =>
        {
            var cosmosSettings = sp.GetRequiredService<IOptions<CosmosSettings>>().Value;
            var client         = sp.GetRequiredService<CosmosClient>();
            var container      = client.GetContainer(cosmosSettings.DatabaseId, BudgetContainerId);
            var logger         = sp.GetRequiredService<ILogger<BudgetGuardService>>();
            return new BudgetGuardService(container, logger);
        });

        // Register as hosted service so ExecuteAsync runs on startup
        services.AddHostedService(sp => sp.GetRequiredService<BudgetGuardService>());

        return services;
    }

    /// <summary>
    /// Ensures the budget Cosmos container exists before the host starts serving requests.
    /// Skips when PersistenceSettings:UseDatabase is false.
    /// Call after app.Build() and before app.RunAsync().
    /// </summary>
    public static async Task InitializeBudgetContainerAsync(this WebApplication app)
    {
        var useDatabase = app.Configuration.GetValue<bool>("PersistenceSettings:UseDatabase");
        if (!useDatabase) { return; }

        var cosmosSettings = app.Services.GetRequiredService<IOptions<CosmosSettings>>().Value;
        var client         = app.Services.GetRequiredService<CosmosClient>();
        var db             = client.GetDatabase(cosmosSettings.DatabaseId);

        await db.CreateContainerIfNotExistsAsync(
            new ContainerProperties(BudgetContainerId, "/id"));

        app.Logger.LogInformation("BudgetGuard: Container '{Container}' ready", BudgetContainerId);
    }
}