using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// DI registration for the budget guard feature.
/// Must be called after AddGamePersistence so CosmosClient is already registered.
/// </summary>
public static class BudgetServiceExtensions
{
    public const string BudgetContainerId = "budget";

    /// <summary>
    /// Registers BudgetGuardService as a hosted singleton and wires its Cosmos container.
    /// The budget container is created in the same database as games and users.
    /// </summary>
    public static IServiceCollection AddBudgetGuard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
    /// Ensures the budget Cosmos container exists on startup.
    /// Call from InitializeGameManagerAsync or app startup before RunAsync.
    /// </summary>
    public static async Task InitializeBudgetContainerAsync(this WebApplication app)
    {
        var cosmosSettings = app.Services.GetRequiredService<IOptions<CosmosSettings>>().Value;
        var client         = app.Services.GetRequiredService<CosmosClient>();
        var db             = client.GetDatabase(cosmosSettings.DatabaseId);

        await db.CreateContainerIfNotExistsAsync(
            new ContainerProperties(BudgetContainerId, "/id"));

        app.Logger.LogInformation("BudgetGuard: Container '{Container}' ready", BudgetContainerId);
    }
}