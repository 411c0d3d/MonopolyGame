using System.Text.Json.Serialization;

namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// Cosmos document tracking monthly Azure Container Apps resource consumption.
/// Partition key is /id — single document, id = "server-budget".
/// </summary>
public sealed class BudgetDocument
{
    public const string DocumentId = "server-budget";

    [JsonPropertyName("id")]
    public string Id { get; set; } = DocumentId;

    /// <summary>
    /// Monthly vCPU-seconds limit (free tier = 180,000).
    /// </summary>
    [JsonPropertyName("maxCpuSeconds")]
    public double MaxCpuSeconds { get; set; } = 162_000; // 90% of 180k

    /// <summary>
    /// Monthly HTTP requests limit (free tier = 2,000,000).
    /// </summary>
    [JsonPropertyName("maxHttpRequests")]
    public long MaxHttpRequests { get; set; } = 1_800_000; // 90% of 2M

    /// <summary>
    /// Hard cap on simultaneous active games.
    /// </summary>
    [JsonPropertyName("maxConcurrentGames")]
    public int MaxConcurrentGames { get; set; } = 10;

    /// <summary>
    /// Hard cap on simultaneous SignalR connections.
    /// </summary>
    [JsonPropertyName("maxConcurrentConnections")]
    public int MaxConcurrentConnections { get; set; } = 80;

    /// <summary>
    /// Accumulated vCPU-seconds consumed this month.
    /// </summary>
    [JsonPropertyName("consumedCpuSeconds")]
    public double ConsumedCpuSeconds { get; set; }

    /// <summary>
    /// Accumulated HTTP requests this month.
    /// </summary>
    [JsonPropertyName("consumedHttpRequests")]
    public long ConsumedHttpRequests { get; set; }

    /// <summary>
    /// UTC start of the current billing month window.
    /// </summary>
    [JsonPropertyName("windowStart")]
    public DateTime WindowStart { get; set; } = GetCurrentMonthStart();

    /// <summary>
    /// UTC timestamp of last Cosmos persist.
    /// </summary>
    [JsonPropertyName("lastSavedAt")]
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;

    public static DateTime GetCurrentMonthStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}