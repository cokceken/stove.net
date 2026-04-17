namespace Stove.Net.Redis;

/// <summary>
/// Configuration options for the Redis system.
/// </summary>
public class RedisSystemOptions
{
    /// <summary>
    /// Maps the exposed container configuration to application config keys.
    /// Receives the container connection string and returns key-value pairs
    /// to inject into the application's configuration.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }

    /// <summary>
    /// Optional cleanup action to run between tests (e.g., FLUSHDB).
    /// </summary>
    public Func<Task>? Cleanup { get; set; }
}
