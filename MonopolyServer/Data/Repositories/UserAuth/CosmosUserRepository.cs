using System.Net;
using Microsoft.Azure.Cosmos;
using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// Cosmos DB implementation of IUserRepository.
/// Users container partition key is /id (B2C objectId).
/// Shares the CosmosClient and StjCosmosSerializer from the game layer.
/// </summary>
public sealed class CosmosUserRepository : IUserRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosUserRepository> _logger;

    /// <summary>
    /// Accepts the already-initialized Cosmos container for users.
    /// </summary>
    public CosmosUserRepository(Container container, ILogger<CosmosUserRepository> logger)
    {
        _container = container;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UserDocument?> GetAsync(string objectId)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(
                id: objectId,
                partitionKey: new PartitionKey(objectId));

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading user {ObjectId} from Cosmos", objectId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<UserDocument?> GetByEmailAsync(string email)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.email = @email")
                .WithParameter("@email", email.ToLowerInvariant());

            using var feed = _container.GetItemQueryIterator<UserDocument>(query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

            if (feed.HasMoreResults)
            {
                var batch = await feed.ReadNextAsync();
                return batch.FirstOrDefault();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying user by email from Cosmos");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(UserDocument user)
    {
        try
        {
            await _container.UpsertItemAsync(user, new PartitionKey(user.Id));
            _logger.LogDebug("Upserted user to Cosmos: {ObjectId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting user {ObjectId} to Cosmos", user.Id);
        }
    }
}