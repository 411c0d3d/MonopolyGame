using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using MonopolyServer.Game.Services;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Data.Repositories.GameStorage;

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
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers IGameRepository — either FileGameRepository or CosmosGameRepository
        /// depending on PersistenceSettings:UseDatabase in configuration.
        /// When UseDatabase is true, also registers CosmosClient as a singleton
        /// so it can be shared with the user repository.
        /// </summary>
        public IServiceCollection AddGamePersistence(IConfiguration configuration)
        {
            var useDatabase = configuration.GetValue<bool>("PersistenceSettings:UseDatabase");

            if (useDatabase)
            {
                services.Configure<CosmosSettings>(configuration.GetSection(CosmosSettings.SectionName));

                // Register CosmosClient as singleton so it is shared across game and user repositories
                services.AddSingleton<CosmosClient>(sp =>
                {
                    var settings = sp.GetRequiredService<IOptions<CosmosSettings>>().Value;
                    var isDevelopment = settings.Endpoint.Contains("localhost");

                    return new CosmosClient(settings.Endpoint, settings.AuthKey, new CosmosClientOptions
                    {
                        Serializer = new StjCosmosSerializer(BuildJsonOptions()),
                        HttpClientFactory = isDevelopment
                            ? () => new HttpClient(new HttpClientHandler
                            {
                                ServerCertificateCustomValidationCallback =
                                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                            })
                            : null,
                        ConnectionMode = isDevelopment ? ConnectionMode.Gateway : ConnectionMode.Direct
                    });
                });

                services.AddSingleton<IGameRepository>(sp =>
                {
                    var settings = sp.GetRequiredService<IOptions<CosmosSettings>>().Value;
                    var client = sp.GetRequiredService<CosmosClient>();
                    var logger = sp.GetRequiredService<ILogger<CosmosGameRepository>>();
                    var repo = new CosmosGameRepository(client, settings, logger);
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
        /// </summary>
        public IServiceCollection AddGameServices()
        {
            services.AddSingleton<GameRoomManager>();
            services.AddHostedService<GameCleanupService>();
            return services;
        }
    }

    /// <summary>
    /// Awaits GameRoomManager.InitializeAsync() before the host begins serving requests.
    /// Call after app.Build() and before app.RunAsync().
    /// </summary>
    public static async Task InitializeGameManagerAsync(this WebApplication app)
    {
        var manager = app.Services.GetRequiredService<GameRoomManager>();
        await manager.InitializeAsync();
    }

    private static System.Text.Json.JsonSerializerOptions BuildJsonOptions()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
}