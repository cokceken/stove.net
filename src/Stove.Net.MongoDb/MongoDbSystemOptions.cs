namespace Stove.Net.MongoDb;

/// <summary>
/// Configuration options for the MongoDB system.
/// </summary>
public class MongoDbSystemOptions
{
    /// <summary>
    /// The database name to use for operations. Defaults to "stove_test".
    /// </summary>
    public string DatabaseName { get; set; } = "stove_test";

    /// <summary>
    /// Maps the exposed container configuration to application config keys.
    /// Receives the container connection string and returns key-value pairs
    /// to inject into the application's configuration.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }

    /// <summary>
    /// Optional cleanup action to run between tests (e.g., drop collections).
    /// </summary>
    public Func<Task>? Cleanup { get; set; }
}
