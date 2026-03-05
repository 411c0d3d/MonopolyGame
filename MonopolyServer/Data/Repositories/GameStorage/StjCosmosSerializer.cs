using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace MonopolyServer.Data.Repositories;

/// <summary>
/// CosmosSerializer backed by System.Text.Json.
/// Replaces the SDK's default Newtonsoft serializer so the entire stack uses one serializer.
/// </summary>
public sealed class StjCosmosSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes the serializer with the provided STJ options.
    /// </summary>
    public StjCosmosSerializer(JsonSerializerOptions options) => _options = options;

    /// <summary>
    /// Deserializes from a Cosmos response stream.
    /// </summary>
    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (typeof(Stream).IsAssignableFrom(typeof(T))) { return (T)(object)stream; }
            return JsonSerializer.Deserialize<T>(stream, _options)!;
        }
    }

    /// <summary>
    /// Serializes to a stream for a Cosmos request.
    /// </summary>
    public override Stream ToStream<T>(T input)
    {
        var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, input, _options);
        ms.Position = 0;
        return ms;
    }
}
