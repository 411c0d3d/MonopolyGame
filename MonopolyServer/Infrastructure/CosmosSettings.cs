namespace MonopolyServer.Infrastructure;

/// <summary>Strongly-typed settings for Cosmos DB, bound from appsettings "CosmosDb" section.</summary>
public sealed class CosmosSettings
{
    /// <summary>
    /// The name of the configuration section in appsettings.json that contains Cosmos DB settings.
    /// </summary>
    public const string SectionName = "CosmosDb";

    /// <summary>
    /// The endpoint URL for the Cosmos DB account.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// The primary key for authenticating with the Cosmos DB account.
    /// </summary>
    public string AuthKey { get; init; } = string.Empty;

    /// <summary>
    /// The name of the Cosmos DB database to use for storing game data.
    /// </summary>
    public string DatabaseId { get; init; } = string.Empty;

    /// <summary>
    /// The name of the Cosmos DB container (collection) to use for storing game data.
    /// </summary>
    public string CollectionId { get; init; } = string.Empty;

    /// <summary>
    /// The name of the Cosmos DB container (collection) to use for storing user data.
    /// </summary>
    public string UsersCollectionId { get; init; } = string.Empty;
}