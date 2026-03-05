using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonopolyServer.Game.Constants;
using MonopolyServer.Game.Models;

namespace MonopolyServer.Data.Repositories;

/// <summary>
/// File-system implementation of IGameRepository.
/// Each game is stored as a single JSON file: /data/games/{gameId}.json
/// Atomic writes use a temp-file-then-replace strategy.
/// Per-game semaphores in _saveLocks serialize concurrent writes to the same file.
/// </summary>
public sealed class FileGameRepository : IGameRepository
{
    private readonly string _gamesDirectory;
    private readonly ILogger<FileGameRepository> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    /// <summary>
    /// Resolves the games directory path and ensures it exists.
    /// </summary>
    public FileGameRepository(ILogger<FileGameRepository> logger)
    {
        _logger = logger;
        _gamesDirectory = Path.Combine(
            AppContext.BaseDirectory,
            GameConstants.DataDirectoryName,
            GameConstants.GamesDirectoryName);

        EnsureDirectoryExists();
    }

    /// <inheritdoc/>
    public Task<GameState?> GetAsync(string gameId)
    {
        var filePath = FilePath(gameId);

        if (!File.Exists(filePath))
        {
            return Task.FromResult<GameState?>(null);
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);
            return Task.FromResult(game);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt game file for {GameId} — deleting", gameId);
            TryDelete(filePath);
            return Task.FromResult<GameState?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading game file {GameId}", gameId);
            return Task.FromResult<GameState?>(null);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<GameState>> GetAllAsync()
    {
        string[] files;

        try
        {
            files = Directory.GetFiles(_gamesDirectory, $"*{GameConstants.GameFileExtension}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating game files");
            return Task.FromResult<IReadOnlyList<GameState>>(Array.Empty<GameState>());
        }

        var results = new List<GameState>();
        var corrupt = new List<string>();

        foreach (var filePath in files)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var game = JsonSerializer.Deserialize<GameState>(json, JsonOptions);

                if (game == null)
                {
                    corrupt.Add(filePath);
                    continue;
                }

                results.Add(game);
            }
            catch (JsonException)
            {
                corrupt.Add(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading game file: {FileName}", Path.GetFileName(filePath));
            }
        }

        foreach (var path in corrupt)
        {
            _logger.LogWarning("Deleting corrupt game file: {FileName}", Path.GetFileName(path));
            TryDelete(path);
        }

        _logger.LogInformation("Loaded {Count} games from disk", results.Count);
        return Task.FromResult<IReadOnlyList<GameState>>(results);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(GameState game)
    {
        var json = JsonSerializer.Serialize(game, JsonOptions);
        var filePath = FilePath(game.GameId);
        var tempPath = filePath + ".tmp";
        var saveLock = _saveLocks.GetOrAdd(game.GameId, _ => new SemaphoreSlim(1, 1));

        await saveLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            File.Replace(tempPath, filePath, destinationBackupFileName: null);
            _logger.LogDebug("Saved game to disk: {GameId}", game.GameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving game {GameId} to disk", game.GameId);
        }
        finally
        {
            saveLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string gameId)
    {
        var filePath = FilePath(gameId);
        TryDelete(filePath);

        if (_saveLocks.TryRemove(gameId, out var sem))
        {
            sem.Dispose();
        }

        return Task.CompletedTask;
    }

    private string FilePath(string gameId) =>
        Path.Combine(_gamesDirectory, $"{gameId}{GameConstants.GameFileExtension}");

    private void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
        }
    }

    private void EnsureDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_gamesDirectory))
            {
                Directory.CreateDirectory(_gamesDirectory);
                _logger.LogInformation("Created games directory: {Directory}", _gamesDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create games directory: {Directory}", _gamesDirectory);
        }
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}