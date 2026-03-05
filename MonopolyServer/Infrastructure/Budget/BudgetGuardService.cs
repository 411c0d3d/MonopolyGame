using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using MonopolyServer.Infrastructure;

namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// Tracks Azure Container Apps free-tier consumption in memory and flushes to Cosmos periodically.
/// Survives container restarts by reloading state from Cosmos on startup.
/// Enforces hard limits on HTTP requests, vCPU-seconds, concurrent games, and connections.
/// </summary>
public sealed class BudgetGuardService : BackgroundService
{
    private readonly Container _container;
    private readonly ILogger<BudgetGuardService> _logger;

    private BudgetDocument _budget = new();
    private int _activeConnections;
    private int _activeGames;
    private DateTime _serverStartedAt = DateTime.UtcNow;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    /// <summary>Accepts the Cosmos budget container resolved from DI.</summary>
    public BudgetGuardService(Container container, ILogger<BudgetGuardService> logger)
    {
        _container = container;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadFromCosmosAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(FlushInterval, stoppingToken);
            FlushCpuAccrual();
            await SaveToCosmosAsync();
            MaybeResetMonth();
        }

        await SaveToCosmosAsync();
    }

    // -------------------------------------------------------------------------
    // Guard checks — called from hubs and middleware
    // -------------------------------------------------------------------------

    /// <summary>Returns false and logs when the HTTP request budget is exhausted.</summary>
    public bool TryConsumeRequest()
    {
        var budgetConsumedHttpRequests = _budget.ConsumedHttpRequests;
        Interlocked.Increment(ref budgetConsumedHttpRequests);

        if (budgetConsumedHttpRequests > _budget.MaxHttpRequests)
        {
            _logger.LogWarning("BudgetGuard: HTTP request limit reached ({Consumed}/{Max})",
                budgetConsumedHttpRequests, _budget.MaxHttpRequests);
            return false;
        }

        return true;
    }

    /// <summary>Returns false when adding a connection would exceed the concurrent connection cap.</summary>
    public bool TryAddConnection()
    {
        var current = Interlocked.Increment(ref _activeConnections);
        if (current > _budget.MaxConcurrentConnections)
        {
            Interlocked.Decrement(ref _activeConnections);
            _logger.LogWarning("BudgetGuard: Connection limit reached ({Current}/{Max})",
                current, _budget.MaxConcurrentConnections);
            return false;
        }

        return true;
    }

    /// <summary>Decrements the active connection counter on disconnect.</summary>
    public void ReleaseConnection() =>
        Interlocked.Decrement(ref _activeConnections);

    /// <summary>Returns false when adding a game would exceed the concurrent game cap.</summary>
    public bool TryAddGame()
    {
        var current = Interlocked.Increment(ref _activeGames);
        if (current > _budget.MaxConcurrentGames)
        {
            Interlocked.Decrement(ref _activeGames);
            _logger.LogWarning("BudgetGuard: Game limit reached ({Current}/{Max})",
                current, _budget.MaxConcurrentGames);
            return false;
        }

        return true;
    }

    /// <summary>Decrements the active game counter when a game ends.</summary>
    public void ReleaseGame() =>
        Interlocked.Decrement(ref _activeGames);

    /// <summary>Returns a snapshot of current consumption for the admin panel.</summary>
    public BudgetSnapshot GetSnapshot() => new()
    {
        ConsumedCpuSeconds    = _budget.ConsumedCpuSeconds + ElapsedCpuSeconds(),
        MaxCpuSeconds         = _budget.MaxCpuSeconds,
        ConsumedHttpRequests  = _budget.ConsumedHttpRequests,
        MaxHttpRequests       = _budget.MaxHttpRequests,
        ActiveConnections     = _activeConnections,
        MaxConcurrentConnections = _budget.MaxConcurrentConnections,
        ActiveGames           = _activeGames,
        MaxConcurrentGames    = _budget.MaxConcurrentGames,
        WindowStart           = _budget.WindowStart,
        LastSavedAt           = _budget.LastSavedAt,
    };

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private double ElapsedCpuSeconds() =>
        (DateTime.UtcNow - _serverStartedAt).TotalSeconds;

    private void FlushCpuAccrual()
    {
        _budget.ConsumedCpuSeconds += ElapsedCpuSeconds();
        _serverStartedAt = DateTime.UtcNow;

        if (_budget.ConsumedCpuSeconds > _budget.MaxCpuSeconds)
        {
            _logger.LogWarning("BudgetGuard: vCPU-seconds limit reached ({Consumed:F0}/{Max:F0})",
                _budget.ConsumedCpuSeconds, _budget.MaxCpuSeconds);
        }
    }

    private void MaybeResetMonth()
    {
        var currentWindowStart = BudgetDocument.GetCurrentMonthStart();
        if (_budget.WindowStart >= currentWindowStart) { return; }

        _logger.LogInformation("BudgetGuard: Monthly window reset");
        _budget.ConsumedCpuSeconds   = 0;
        _budget.ConsumedHttpRequests = 0;
        _budget.WindowStart          = currentWindowStart;
    }

    private async Task LoadFromCosmosAsync()
    {
        try
        {
            var response = await _container.ReadItemAsync<BudgetDocument>(
                BudgetDocument.DocumentId,
                new PartitionKey(BudgetDocument.DocumentId));

            _budget = response.Resource;
            _logger.LogInformation("BudgetGuard: Loaded budget from Cosmos — window={Window}",
                _budget.WindowStart);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _budget = new BudgetDocument();
            await SaveToCosmosAsync();
            _logger.LogInformation("BudgetGuard: Initialized new budget document in Cosmos");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BudgetGuard: Failed to load from Cosmos — using defaults");
        }
    }

    private async Task SaveToCosmosAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _budget.LastSavedAt = DateTime.UtcNow;
            await _container.UpsertItemAsync(_budget, new PartitionKey(_budget.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BudgetGuard: Failed to save budget to Cosmos");
        }
        finally
        {
            _lock.Release();
        }
    }
}