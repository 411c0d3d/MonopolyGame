namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// Read-only snapshot of current budget consumption for the admin panel.
/// </summary>
public sealed class BudgetSnapshot
{
    /// <summary>
    /// Consumed CPU seconds and HTTP requests are the total amounts used since the start of the current billing cycle.
    /// </summary>
    public double ConsumedCpuSeconds     { get; init; }
    
    /// <summary>
    /// Max CPU seconds and HTTP requests represent the total budget limits for the current billing cycle, as defined by the Azure subscription and service limits. These values are used to calculate the percentage of budget consumed and to provide insights into resource usage trends over time.
    /// </summary>
    public double MaxCpuSeconds          { get; init; }
    
    /// <summary>
    /// Consumed HTTP requests and Max HTTP requests represent the total number of HTTP requests made to the service during the current billing cycle and the maximum allowed requests based on the Azure subscription and service limits, respectively. These metrics are crucial for monitoring API usage patterns, identifying potential bottlenecks, and ensuring that the service operates within the allocated budget constraints.
    /// </summary>
    public long   ConsumedHttpRequests   { get; init; }
    
    /// <summary>
    /// Max HTTP requests represent the total number of HTTP requests allowed for the service during the current billing cycle, based on the Azure subscription and service limits. This value is essential for calculating the percentage of budget consumed, monitoring API usage patterns, and ensuring that the service operates within the allocated budget constraints. By comparing Consumed HTTP requests with Max HTTP requests, administrators can identify potential bottlenecks, optimize resource usage, and make informed decisions about scaling or adjusting service configurations to meet demand while staying within budget limits.
    /// </summary>
    public long   MaxHttpRequests        { get; init; }
    
    /// <summary>
    /// Active connections and games represent the current number of concurrent users and game sessions interacting with the service. Max concurrent connections and games indicate the maximum allowed concurrent users and game sessions based on the Azure subscription and service limits. These metrics are vital for monitoring real-time usage patterns, ensuring optimal performance, and maintaining a positive user experience while adhering to budget constraints. By tracking active connections and games against their respective maximums, administrators can identify trends, anticipate resource needs, and make informed decisions about scaling or adjusting service configurations to accommodate user demand while staying within budget limits.
    /// </summary>
    public int    ActiveConnections      { get; init; }
    
    
    /// <summary>
    /// Max concurrent connections represent the maximum number of concurrent users allowed to interact with the service during the current billing cycle, based on the Azure subscription and service limits. This value is crucial for monitoring real-time usage patterns, ensuring optimal performance, and maintaining a positive user experience while adhering to budget constraints. By comparing active connections with max concurrent connections, administrators can identify trends, anticipate resource needs, and make informed decisions about scaling or adjusting service configurations to accommodate user demand while staying within budget limits.
    /// </summary>
    public int    MaxConcurrentConnections { get; init; }
    
    
    
    /// <summary>
    /// Active games and Max concurrent games represent the current number of game sessions in progress and the maximum allowed concurrent game sessions based on the Azure subscription and service limits, respectively. These metrics are essential for monitoring real-time usage patterns, ensuring optimal performance, and maintaining a positive user experience while adhering to budget constraints. By tracking active games against max concurrent games, administrators can identify trends, anticipate resource needs, and make informed decisions about scaling or adjusting service configurations to accommodate user demand while staying within budget limits.
    /// </summary>
    public int    ActiveGames            { get; init; }
    
    
    /// <summary>
    /// Max concurrent games represent the maximum number of game sessions allowed to be in progress simultaneously during the current billing cycle, based on the Azure subscription and service limits. This value is crucial for monitoring real-time usage patterns, ensuring optimal performance, and maintaining a positive user experience while adhering to budget constraints. By comparing active games with max concurrent games, administrators can identify trends, anticipate resource needs, and make informed decisions about scaling or adjusting service configurations to accommodate user demand while staying within budget limits.
    /// </summary>
    public int    MaxConcurrentGames     { get; init; }
    
    /// <summary>
    /// WindowStart represents the timestamp marking the beginning of the current billing cycle, while LastSavedAt indicates the most recent time when the budget snapshot was updated. These timestamps are crucial for contextualizing the budget consumption data, allowing administrators to track resource usage trends over time, identify patterns in user behavior, and make informed decisions about scaling or adjusting service configurations to optimize performance and stay within budget limits. By analyzing the time elapsed since WindowStart and the frequency of updates indicated by LastSavedAt, administrators can gain insights into how quickly resources are being consumed and whether adjustments are needed to accommodate user demand while adhering to budget constraints.
    /// </summary>
    public DateTime WindowStart          { get; init; }
    
    /// <summary>
    /// LastSavedAt indicates the most recent time when the budget snapshot was updated, providing a reference point for the freshness of the data. This timestamp is crucial for administrators to understand how current the budget consumption information is, allowing them to make informed decisions about scaling or adjusting service configurations based on real-time usage patterns. By comparing LastSavedAt with WindowStart, administrators can assess how quickly resources are being consumed within the current billing cycle and whether adjustments are needed to accommodate user demand while adhering to budget constraints.
    /// </summary>
    public DateTime LastSavedAt          { get; init; }

    /// <summary>
    /// CpuPercent and RequestPercent represent the percentage of the allocated CPU seconds and HTTP requests that have been consumed during the current billing cycle, calculated by comparing ConsumedCpuSeconds with MaxCpuSeconds and ConsumedHttpRequests with MaxHttpRequests, respectively. These percentages are essential for administrators to quickly assess how much of the allocated budget has been used, identify potential bottlenecks, and make informed decisions about scaling or adjusting service configurations to optimize performance and stay within budget limits. By monitoring CpuPercent and RequestPercent, administrators can proactively manage resources, anticipate future needs, and ensure a positive user experience while adhering to budget constraints.
    /// </summary>
    public double CpuPercent     => MaxCpuSeconds > 0 ? ConsumedCpuSeconds / MaxCpuSeconds * 100 : 0;
    
    /// <summary>
    /// RequestPercent represents the percentage of the allocated HTTP requests that have been consumed during the current billing cycle, calculated by comparing ConsumedHttpRequests with MaxHttpRequests. This percentage is crucial for administrators to quickly assess how much of the allocated budget for HTTP requests has been used, identify potential bottlenecks in API usage, and make informed decisions about scaling or adjusting service configurations to optimize performance and stay within budget limits. By monitoring RequestPercent, administrators can proactively manage API resources, anticipate future needs, and ensure a positive user experience while adhering to budget constraints.
    /// </summary>
    public double RequestPercent => MaxHttpRequests > 0 ? (double)ConsumedHttpRequests / MaxHttpRequests * 100 : 0;
}