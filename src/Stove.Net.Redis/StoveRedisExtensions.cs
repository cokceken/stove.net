using Stove.Net.Core;

namespace Stove.Net.Redis;

/// <summary>
/// Extension methods to register and access the Redis system.
/// </summary>
public static class StoveRedisExtensions
{
    extension(StoveBuilder builder)
    {
        /// <summary>
        /// Register a Redis system (with Testcontainers) in the Stove builder.
        /// </summary>
        public StoveBuilder WithRedis(Action<RedisSystemOptions>? configure = null) =>
            builder.WithRedis(SystemKey.DefaultName, configure);

        /// <summary>
        /// Register a named Redis system. Use when you need multiple Redis instances.
        /// </summary>
        public StoveBuilder WithRedis(string name,
            Action<RedisSystemOptions>? configure = null)
        {
            var options = new RedisSystemOptions();
            configure?.Invoke(options);
            builder.WithSystem(new RedisSystem(options), name);
            return builder;
        }
    }

    extension(ValidationDsl dsl)
    {
        /// <summary>
        /// Access the default Redis system in a validation block.
        /// </summary>
        public async Task Redis(Func<RedisSystem, Task> validation)
        {
            await validation(dsl.Get<RedisSystem>());
        }

        /// <summary>
        /// Access a named Redis system in a validation block.
        /// </summary>
        public async Task Redis(string name, Func<RedisSystem, Task> validation)
        {
            await validation(dsl.Get<RedisSystem>(name));
        }
    }
}