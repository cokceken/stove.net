using Stove.Net.Core;

namespace Stove.Net.Dashboard;

/// <summary>
/// Extension methods for adding the Stove dashboard to a StoveBuilder.
/// </summary>
public static class StoveDashboardExtensions
{
    /// <summary>
    /// Enable streaming of test events to the Stove dashboard UI.
    /// The dashboard must be running at the configured address (default http://localhost:4041).
    /// If it is unreachable the emitter self-disables silently — the test run is never blocked.
    /// </summary>
    public static StoveBuilder WithDashboard(
        this StoveBuilder builder,
        Action<DashboardSystemOptions>? configure = null)
    {
        var options = new DashboardSystemOptions();
        configure?.Invoke(options);
        return builder.WithDashboard(options);
    }

    /// <summary>
    /// Enable streaming of test events to the Stove dashboard UI with pre-built options.
    /// </summary>
    public static StoveBuilder WithDashboard(
        this StoveBuilder builder,
        DashboardSystemOptions options)
    {
        var system = new DashboardSystem(options);
        builder.WithSystem(system);
        builder.WithListener(system);
        return builder;
    }
}
