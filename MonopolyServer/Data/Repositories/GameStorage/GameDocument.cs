using System.Text.Json.Serialization;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Data.Repositories;

/// <summary>
/// Cosmos document wrapper for GameState.
/// 'id' maps to the Cosmos partition key field.
/// GameState is stored as a proper nested object via StjCosmosSerializer — not a serialized string.
/// </summary>
public sealed class GameDocument
{
    [JsonPropertyName("id")] 
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("gameState")] 
    public GameState? GameState { get; set; }
}