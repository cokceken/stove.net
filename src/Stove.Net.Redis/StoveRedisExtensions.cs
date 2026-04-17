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

        var system = new RedisSystem(options);
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the Redis system in a validation block.
    /// </summary>
    public static async Task Redis(
        this ValidationDsl dsl,
        Func<RedisSystem, Task> validation)
    {
        var system = dsl.Get<RedisSystem>();
        await validation(system);
    }
}
