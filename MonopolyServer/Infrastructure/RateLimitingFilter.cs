using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace MonopolyServer.Infrastructure;

/// <summary>
/// Rate limiting filter for SignalR hub methods to prevent spam and abuse.
/// Tracks requests per connection and enforces configurable limits.
/// </summary>
public class RateLimitingFilter : IHubFilter
{
    private readonly ILogger<RateLimitingFilter> _logger;
    private readonly ConcurrentDictionary<string, ConnectionRateLimit> _rateLimits = new();

    // Rate limit configuration
    private const int MaxRequestsPerSecond = 10;
    private const int MaxRequestsPerMinute = 100;
    private const int RateLimitWindowSeconds = 1;
    private const int RateLimitWindowMinutes = 1;

    public RateLimitingFilter(ILogger<RateLimitingFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connectionId = invocationContext.Context.ConnectionId;
        var methodName = invocationContext.HubMethodName;

        // Get or create rate limit tracker for this connection
        var rateLimit = _rateLimits.GetOrAdd(connectionId, _ => new ConnectionRateLimit());

        // Check if rate limit is exceeded
        if (rateLimit.IsRateLimitExceeded())
        {
            _logger.LogWarning($"Rate limit exceeded for connection {connectionId} on method {methodName}");
            throw new HubException("Rate limit exceeded. Please slow down.");
        }

        // Record this request
        rateLimit.RecordRequest();

        // Proceed with the method invocation
        return await next(invocationContext);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        return next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        // Clean up rate limit data for disconnected connection
        _rateLimits.TryRemove(context.Context.ConnectionId, out _);
        return next(context, exception);
    }

    /// <summary>
    /// Tracks request rates for a single connection.
    /// </summary>
    private class ConnectionRateLimit
    {
        private readonly Queue<DateTime> _requestTimestamps = new();
        private readonly object _lock = new();

        public void RecordRequest()
        {
            lock (_lock)
            {
                _requestTimestamps.Enqueue(DateTime.UtcNow);
                CleanOldRequests();
            }
        }

        public bool IsRateLimitExceeded()
        {
            lock (_lock)
            {
                CleanOldRequests();

                var now = DateTime.UtcNow;
                var lastSecond = _requestTimestamps.Count(t => (now - t).TotalSeconds <= RateLimitWindowSeconds);
                var lastMinute = _requestTimestamps.Count(t => (now - t).TotalMinutes <= RateLimitWindowMinutes);

                return lastSecond > MaxRequestsPerSecond || lastMinute > MaxRequestsPerMinute;
            }
        }

        private void CleanOldRequests()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-RateLimitWindowMinutes);
            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoff)
            {
                _requestTimestamps.Dequeue();
            }
        }
    }
}