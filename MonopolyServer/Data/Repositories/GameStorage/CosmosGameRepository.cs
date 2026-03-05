using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using MonopolyServer.Game.Models;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Data.Repositories.GameStorage;

/// <summary>
/// Cosmos DB implementation of IGameRepository using Microsoft.Azure.Cosmos v3.
/// GameState is stored as a proper nested JSON document via a custom STJ CosmosSerializer,
/// eliminating the double-serialized string payload and making the state fully queryable.
/// Partition key is /id — one logical partition per game.
/// Call InitializeAsync() at startup before serving requests.
/// </summary>
public sealed class CosmosGameRepository : IGameRepository
{
    private readonly CosmosClient _client;
    private readonly string _databaseId;
    private readonly string _collectionId;
    private readonly ILogger<CosmosGameRepository> _logger;
    private Container _container = null!;

    /// <summary>Accepts the shared CosmosClient registered in DI.</summary>
    public CosmosGameRepository(CosmosClient client, CosmosSettings settings, ILogger<CosmosGameRepository> logger)
    {
        _client = client;
        _logger = logger;
        _databaseId = settings.DatabaseId;
        _collectionId = settings.CollectionId;
    }

    /// <summary>Ensures the database and partitioned games container exist. Call once at startup.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var dbResponse = await _client.CreateDatabaseIfNotExistsAsync(_databaseId);

            _container = await dbResponse.Database.CreateContainerIfNotExistsAsync(
                id: _collectionId,
                partitionKeyPath: "/id",
                throughput: 400);

            // Ensure users container also exists
            await dbResponse.Database.CreateContainerIfNotExistsAsync(
                id: _collectionId.Replace("games", "users"),
                partitionKeyPath: "/id",
                throughput: 400);

            _logger.LogInformation(
                "Cosmos DB ready: {Database}/{Collection}", _databaseId, _collectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Cosmos DB {Database}/{Collection}",
                _databaseId, _collectionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<GameState?> GetAsync(string gameId)
    {
        try
        {
            var response = await _container.ReadItemAsync<GameDocument>(
                id: gameId,
                partitionKey: new PartitionKey(gameId));

            return response.Resource.GameState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading game {GameId} from Cosmos", gameId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GameState>> GetAllAsync()
    {
        var results = new List<GameState>();
        var corruptIds = new List<string>();

        try
        {
            using var feed = _container.GetItemQueryIterator<GameDocument>("SELECT * FROM c");

            while (feed.HasMoreResults)
            {
                var batch = await feed.ReadNextAsync();
                foreach (var doc in batch)
                {
                    if (doc.GameState == null)
                    {
                        corruptIds.Add(doc.Id);
                        continue;
                    }

                    results.Add(doc.GameState);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying games from Cosmos");
        }

        foreach (var id in corruptIds)
        {
            _logger.LogWarning("Deleting corrupt game document: {GameId}", id);
            await DeleteAsync(id);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(GameState game)
    {
        try
        {
            var doc = new GameDocument { Id = game.GameId, GameState = game };
            await _container.UpsertItemAsync(doc, new PartitionKey(game.GameId));
            _logger.LogDebug("Upserted game to Cosmos: {GameId}", game.GameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting game {GameId} to Cosmos", game.GameId);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string gameId)
    {
        try
        {
            await _container.DeleteItemAsync<GameDocument>(
                id: gameId,
                partitionKey: new PartitionKey(gameId));

            _logger.LogInformation("Deleted game document from Cosmos: {GameId}", gameId);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting game {GameId} from Cosmos", gameId);
        }
    }
}