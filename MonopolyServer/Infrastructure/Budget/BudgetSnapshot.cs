namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// Read-only snapshot of current budget consumption for the admin panel.
/// Computed on demand from live in-memory counters — not a Cosmos read.
/// </summary>
public sealed class BudgetSnapshot
{
    /// <summary>
    /// Accumulated vCPU-seconds consumed this billing window,
    /// including accrual since the last flush.
    /// </summary>
    public double ConsumedCpuSeconds { get; init; }

    /// <summary>
    /// Monthly vCPU-seconds budget ceiling.
    /// </summary>
    public double MaxCpuSeconds { get; init; }

    /// <summary>
    /// Total HTTP requests counted this billing window.
    /// </summary>
    public long ConsumedHttpRequests { get; init; }

    /// <summary>
    /// Monthly HTTP request budget ceiling.
    /// </summary>
    public long MaxHttpRequests { get; init; }

    /// <summary>
    /// Current number of active SignalR connections.
    /// </summary>
    public int ActiveConnections { get; init; }

    /// <summary>
    /// Hard cap on simultaneous SignalR connections.
    /// </summary>
    public int MaxConcurrentConnections { get; init; }

    /// <summary>
    /// Current number of active game sessions.
    /// </summary>
    public int ActiveGames { get; init; }

    /// <summary>
    /// Hard cap on simultaneous active game sessions.
    /// </summary>
    public int MaxConcurrentGames { get; init; }

    /// <summary>
    /// UTC start of the current billing month window.
    /// </summary>
    public DateTime WindowStart { get; init; }

    /// <summary>
    /// UTC timestamp of the last successful Cosmos flush.
    /// </summary>
    public DateTime LastSavedAt { get; init; }

    /// <summary>
    /// Percentage of the monthly vCPU-seconds budget consumed.
    /// </summary>
    public double CpuPercent => MaxCpuSeconds > 0 ? ConsumedCpuSeconds / MaxCpuSeconds * 100 : 0;

    /// <summary>
    /// Percentage of the monthly HTTP request budget consumed.
    /// </summary>
    public double RequestPercent => MaxHttpRequests > 0 ? (double)ConsumedHttpRequests / MaxHttpRequests * 100 : 0;
}