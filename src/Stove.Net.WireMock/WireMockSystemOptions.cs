namespace Stove.Net.WireMock;

/// <summary>
/// Configuration options for the WireMock system.
/// </summary>
public class WireMockSystemOptions
{
    /// <summary>
    /// Maps the WireMock server URL to application config keys.
    /// Receives the server base URL (e.g., "http://localhost:12345") and returns
    /// key-value pairs to inject into the application's configuration.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }

    /// <summary>
    /// Optional cleanup action to run between tests (e.g., reset mappings).
    /// By default, all mappings and log entries are reset.
    /// Set to a custom action to override, or null to disable automatic cleanup.
    /// </summary>
    public bool ResetOnCleanup { get; set; } = true;

    /// <summary>
    /// Optional fixed port for the WireMock server. If null, a random port is used.
    /// </summary>
    public int? Port { get; set; }
}
