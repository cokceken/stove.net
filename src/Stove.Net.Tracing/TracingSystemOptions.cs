namespace Stove.Net.Tracing;

/// <summary>
/// Configuration options for the OTLP tracing system.
/// </summary>
public sealed class TracingSystemOptions
{
    /// <summary>
    /// Port for the OTLP gRPC receiver. Default: 0 (auto-assign available port).
    /// Set to a specific port (e.g., 4317) for deterministic configuration.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Maximum time to wait for spans to arrive after a test step. Default: 2 seconds.
    /// </summary>
    public TimeSpan SpanCollectionTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Polling interval when waiting for spans. Default: 50ms.
    /// </summary>
    public TimeSpan SpanPollInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Additional time to wait for straggler spans after the first spans arrive. Default: 200ms.
    /// </summary>
    public TimeSpan StragglerWaitTime { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Maximum spans to store per trace. Default: 1000.
    /// </summary>
    public int MaxSpansPerTrace { get; set; } = 1000;

    /// <summary>
    /// Batch export delay in milliseconds. Set low for testing so spans arrive quickly.
    /// Default: 100ms (vs OTel SDK default of 5000ms).
    /// </summary>
    public int BatchExportDelayMs { get; set; } = 100;

    /// <summary>
    /// Configuration keys to expose to the application under test.
    /// Maps the OTLP endpoint and protocol into the app's IConfiguration.
    /// By default, uses standard OTel environment variable names.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }
}
