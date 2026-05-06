using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using Stove.Net.Dashboard;

namespace Stove.Net.Tracing;

/// <summary>
/// Extension methods for adding OTLP tracing to the Stove builder and validation DSL.
/// </summary>
public static class StoveTracingExtensions
{
    /// <summary>
    /// Add the OTLP tracing system and dashboard. This spins up a gRPC receiver that the
    /// application under test exports traces to. The receiver endpoint is
    /// automatically exposed as OTEL_EXPORTER_OTLP_ENDPOINT configuration.
    /// The dashboard listener is registered automatically to visualize test events and spans.
    /// </summary>
    public static StoveBuilder WithOtlpTracing(this StoveBuilder builder,
        Action<TracingSystemOptions>? configure = null,
        Action<DashboardSystemOptions>? configureDashboard = null)
    {
        var options = new TracingSystemOptions();
        configure?.Invoke(options);
        var system = new TracingSystem(options);
        builder.WithSystem(system);

        var dashboardOptions = new DashboardSystemOptions();
        configureDashboard?.Invoke(dashboardOptions);
        builder.WithDashboard(dashboardOptions);

        return builder;
    }

    /// <summary>Access the TracingSystem from the validation DSL.</summary>
    public static TracingSystem Tracing(this ValidationDsl dsl)
        => dsl.Get<TracingSystem>();
}
