namespace Stove.Net.Dashboard;

/// <summary>
/// Configuration for connecting to the Stove dashboard UI.
/// The dashboard must be running locally (typically started with `docker run trendyol/stove-dashboard`).
/// </summary>
public sealed class DashboardSystemOptions
{
    /// <summary>
    /// Logical application name shown in the dashboard. Defaults to the entry assembly name.
    /// </summary>
    public string AppName { get; set; } = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Stove.Net";

    /// <summary>Host where the Stove dashboard gRPC server is running. Default: localhost.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>gRPC port of the Stove dashboard. Default: 4041.</summary>
    public int Port { get; set; } = 4041;

    /// <summary>
    /// Number of consecutive send failures before the emitter self-disables.
    /// This prevents test noise when the dashboard is not running. Default: 5.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Maximum time to wait for in-flight events to drain when the run ends. Default: 30 seconds.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal string Address => $"http://{Host}:{Port}";
}
