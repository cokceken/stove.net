namespace Stove.Net.Kafka;

/// <summary>
/// Configuration options for the Kafka system.
/// </summary>
public class KafkaSystemOptions
{
    /// <summary>
    /// Topics the background consumer should subscribe to for message capture.
    /// Messages on these topics can be asserted with <c>ShouldBePublished</c>.
    /// </summary>
    public List<string> TopicsToConsume { get; } = new();

    /// <summary>
    /// Maps the exposed container configuration to application config keys.
    /// Receives the bootstrap servers string and returns key-value pairs
    /// to inject into the application's configuration.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }

    /// <summary>
    /// Optional cleanup action to run between tests.
    /// </summary>
    public Func<Task>? Cleanup { get; set; }

    /// <summary>
    /// Default timeout for message assertions (ShouldBePublished).
    /// Defaults to 10 seconds.
    /// </summary>
    public TimeSpan AssertionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
