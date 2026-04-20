using Stove.Net.Core;

namespace Stove.Net.Redis;

/// <summary>
/// Extension methods to register and access the Redis system.
/// </summary>
public static class StoveRedisExtensions
{
    /// <summary>
    /// Register a Redis system (with Testcontainers) in the Stove builder.
    /// </summary>
    public static StoveBuilder WithRedis(
        this StoveBuilder builder,
        Action<RedisSystemOptions>? configure = null)
    {
        var options = new RedisSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new RedisSystem(options));
        return builder;
    }

    /// <summary>
    /// Register a named Redis system. Use when you need multiple Redis instances.
    /// </summary>
    public static StoveBuilder WithRedis(
        this StoveBuilder builder,
        string name,
        Action<RedisSystemOptions>? configure = null)
    {
        var options = new RedisSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new RedisSystem(options), name);
        return builder;
    }

    /// <summary>
    /// Access the default Redis system in a validation block.
    /// </summary>
    public static async Task Redis(this ValidationDsl dsl, Func<RedisSystem, Task> validation)
    {
        await validation(dsl.Get<RedisSystem>());
    }

    /// <summary>
    /// Access a named Redis system in a validation block.
    /// </summary>
    public static async Task Redis(this ValidationDsl dsl, string name, Func<RedisSystem, Task> validation)
    {
        await validation(dsl.Get<RedisSystem>(name));
    }
}
