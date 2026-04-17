using System.Text.Json;
using DotNet.Testcontainers.Images;
using StackExchange.Redis;
using Stove.Net.Core;
using Testcontainers.Redis;

namespace Stove.Net.Redis;

/// <summary>
/// Redis system using Testcontainers. Manages a Redis container
/// and provides get/set/assertion methods for e2e testing.
/// </summary>
public class RedisSystem(RedisSystemOptions options) : IPluggedSystem, IExposesConfiguration
{
    private RedisContainer? _container;
    private string? _connectionString;
    private ConnectionMultiplexer? _multiplexer;

    /// <summary>
    /// The container's connection string, available after RunAsync().
    /// </summary>
    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException(
                                          "Redis container is not started yet.");

    public async Task RunAsync()
    {
        _container = new RedisBuilder(new DockerImage("redis:7-alpine")).Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_connectionString);
    }

    public async Task CleanupAsync()
    {
        if (options.Cleanup != null)
            await options.Cleanup();
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _connectionString != null)
            return options.ConfigureExposedConfiguration(_connectionString);

        if (_connectionString != null)
        {
            return
            [
                new KeyValuePair<string, string>("Redis:ConnectionString", _connectionString)
            ];
        }

        return [];
    }

    private IDatabase GetDatabase() =>
        _multiplexer?.GetDatabase()
        ?? throw new InvalidOperationException("Redis container is not started yet.");

    // --- Set ---

    /// <summary>
    /// Set a string value in Redis.
    /// </summary>
    public async Task<RedisSystem> SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        var db = GetDatabase();
        await db.StringSetAsync(key, value);
        if (expiry.HasValue)
            await db.KeyExpireAsync(key, expiry.Value);
        return this;
    }

    /// <summary>
    /// Set a JSON-serialized value in Redis.
    /// </summary>
    public async Task<RedisSystem> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value);
        var db = GetDatabase();
        await db.StringSetAsync(key, json);
        if (expiry.HasValue)
            await db.KeyExpireAsync(key, expiry.Value);
        return this;
    }

    // --- Get ---

    /// <summary>
    /// Get a string value from Redis and validate it.
    /// </summary>
    public async Task<RedisSystem> GetAsync(string key, Action<string?> validate)
    {
        var value = await GetDatabase().StringGetAsync(key);
        validate(value.HasValue ? value.ToString() : null);
        return this;
    }

    /// <summary>
    /// Get a JSON-deserialized value from Redis and validate it.
    /// </summary>
    public async Task<RedisSystem> GetAsync<T>(string key, Action<T?> validate)
    {
        var value = await GetDatabase().StringGetAsync(key);
        if (!value.HasValue)
        {
            validate(default);
            return this;
        }

        var deserialized = JsonSerializer.Deserialize<T>(value.ToString());
        validate(deserialized);
        return this;
    }

    // --- Assertions ---

    /// <summary>
    /// Assert that a key exists in Redis.
    /// </summary>
    public async Task<RedisSystem> ShouldExist(string key)
    {
        var exists = await GetDatabase().KeyExistsAsync(key);
        if (!exists)
            throw new InvalidOperationException($"Expected key '{key}' to exist in Redis, but it was not found.");
        return this;
    }

    /// <summary>
    /// Assert that a key does not exist in Redis.
    /// </summary>
    public async Task<RedisSystem> ShouldNotExist(string key)
    {
        var exists = await GetDatabase().KeyExistsAsync(key);
        if (exists)
            throw new InvalidOperationException($"Expected key '{key}' to not exist in Redis, but it was found.");
        return this;
    }

    /// <summary>
    /// Delete a key from Redis.
    /// </summary>
    public async Task<RedisSystem> DeleteAsync(string key)
    {
        await GetDatabase().KeyDeleteAsync(key);
        return this;
    }

    // --- Hash ---

    /// <summary>
    /// Set a hash field in Redis.
    /// </summary>
    public async Task<RedisSystem> HashSetAsync(string key, string field, string value)
    {
        await GetDatabase().HashSetAsync(key, field, value);
        return this;
    }

    /// <summary>
    /// Get all hash entries for a key and validate them.
    /// </summary>
    public async Task<RedisSystem> HashGetAllAsync(string key, Action<Dictionary<string, string>> validate)
    {
        var entries = await GetDatabase().HashGetAllAsync(key);
        var dict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        validate(dict);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer != null)
            await _multiplexer.DisposeAsync();

        if (_container != null)
            await _container.DisposeAsync();
    }
}
