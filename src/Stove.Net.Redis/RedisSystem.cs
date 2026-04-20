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
public class RedisSystem(RedisSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem
{
    private const string SystemName = "Redis";
    private RedisContainer? _container;
    private string? _connectionString;
    private ConnectionMultiplexer? _multiplexer;
    private IStoveEventEmitter? _emitter;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    /// <summary>The container's connection string, available after RunAsync().</summary>
    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException("Redis container is not started yet.");

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
            return [new KeyValuePair<string, string>("Redis:ConnectionString", _connectionString)];

        return [];
    }

    private IDatabase GetDatabase() =>
        _multiplexer?.GetDatabase()
        ?? throw new InvalidOperationException("Redis container is not started yet.");

    // --- Set ---

    public async Task<RedisSystem> SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            var db = GetDatabase();
            await db.StringSetAsync(key, value);
            if (expiry.HasValue) await db.KeyExpireAsync(key, expiry.Value);
            Emit("Set", key, "ok");
        }
        catch (Exception ex) when (EmitFailure("Set", key, ex)) { }
        return this;
    }

    public async Task<RedisSystem> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            var db = GetDatabase();
            await db.StringSetAsync(key, json);
            if (expiry.HasValue) await db.KeyExpireAsync(key, expiry.Value);
            Emit("Set", key, "ok");
        }
        catch (Exception ex) when (EmitFailure("Set", key, ex)) { }
        return this;
    }

    // --- Get ---

    public async Task<RedisSystem> GetAsync(string key, Action<string?> validate)
    {
        try
        {
            var value = await GetDatabase().StringGetAsync(key);
            var str = value.HasValue ? value.ToString() : null;
            validate(str);
            Emit("Get", key, str ?? "(null)");
        }
        catch (Exception ex) when (EmitFailure("Get", key, ex)) { }
        return this;
    }

    public async Task<RedisSystem> GetAsync<T>(string key, Action<T?> validate)
    {
        try
        {
            var value = await GetDatabase().StringGetAsync(key);
            T? deserialized = default;
            if (value.HasValue) deserialized = JsonSerializer.Deserialize<T>(value.ToString());
            validate(deserialized);
            Emit("Get", key, value.HasValue ? value.ToString() : "(null)");
        }
        catch (Exception ex) when (EmitFailure("Get", key, ex)) { }
        return this;
    }

    // --- Assertions ---

    public async Task<RedisSystem> ShouldExist(string key)
    {
        try
        {
            var exists = await GetDatabase().KeyExistsAsync(key);
            if (!exists) throw new InvalidOperationException($"Expected key '{key}' to exist in Redis, but it was not found.");
            Emit("ShouldExist", key, "exists");
        }
        catch (Exception ex) when (EmitFailure("ShouldExist", key, ex)) { }
        return this;
    }

    public async Task<RedisSystem> ShouldNotExist(string key)
    {
        try
        {
            var exists = await GetDatabase().KeyExistsAsync(key);
            if (exists) throw new InvalidOperationException($"Expected key '{key}' to not exist in Redis, but it was found.");
            Emit("ShouldNotExist", key, "not found");
        }
        catch (Exception ex) when (EmitFailure("ShouldNotExist", key, ex)) { }
        return this;
    }

    public async Task<RedisSystem> DeleteAsync(string key)
    {
        try
        {
            await GetDatabase().KeyDeleteAsync(key);
            Emit("Delete", key, "ok");
        }
        catch (Exception ex) when (EmitFailure("Delete", key, ex)) { }
        return this;
    }

    // --- Hash ---

    public async Task<RedisSystem> HashSetAsync(string key, string field, string value)
    {
        try
        {
            await GetDatabase().HashSetAsync(key, field, value);
            Emit("HashSet", $"{key}:{field}", "ok");
        }
        catch (Exception ex) when (EmitFailure("HashSet", $"{key}:{field}", ex)) { }
        return this;
    }

    public async Task<RedisSystem> HashGetAllAsync(string key, Action<Dictionary<string, string>> validate)
    {
        try
        {
            var entries = await GetDatabase().HashGetAllAsync(key);
            var dict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
            validate(dict);
            Emit("HashGetAll", key, $"{dict.Count} field(s)");
        }
        catch (Exception ex) when (EmitFailure("HashGetAll", key, ex)) { }
        return this;
    }

    // --- Fault Injection ---

    public async Task<RedisSystem> SetMaxMemory(string maxMemory)
    {
        var server = GetServer();
        await server.ConfigSetAsync("maxmemory", maxMemory);
        return this;
    }

    public async Task<RedisSystem> SetMaxMemoryPolicy(string policy)
    {
        var server = GetServer();
        await server.ConfigSetAsync("maxmemory-policy", policy);
        return this;
    }

    public async Task<RedisSystem> SetIdleTimeout(int seconds)
    {
        var server = GetServer();
        await server.ConfigSetAsync("timeout", seconds.ToString());
        return this;
    }

    public async Task<RedisSystem> SimulateSlowCommand(TimeSpan duration)
    {
        var server = GetServer();
        await server.ExecuteAsync("DEBUG", "SLEEP", duration.TotalSeconds.ToString("F1"));
        return this;
    }

    private IServer GetServer()
    {
        var config = ConfigurationOptions.Parse(ConnectionString);
        config.AllowAdmin = true;
        var adminMultiplexer = ConnectionMultiplexer.Connect(config);
        var endpoint = adminMultiplexer.GetEndPoints()[0];
        return adminMultiplexer.GetServer(endpoint);
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer != null) await _multiplexer.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    private void Emit(string action, string? input, string? output)
        => _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Success, Input = input, Output = output
        });

    private bool EmitFailure(string action, string? input, Exception ex)
    {
        _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Failed, Input = input, Error = ex.Message
        });
        return false;
    }
}
