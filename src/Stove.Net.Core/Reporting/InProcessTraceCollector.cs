using System.Collections.Concurrent;
using System.Diagnostics;

namespace Stove.Net.Core.Reporting;

/// <summary>
/// Captures server-side traces from in-process SUTs (e.g., WebApplicationFactory) using
/// System.Diagnostics.ActivityListener. Converts completed Activity objects to StoveSpan
/// records and emits them through the Stove event bus.
///
/// Works with any .NET library that emits Activity spans:
/// ASP.NET Core, HttpClient, EF Core, Npgsql, MongoDB, StackExchange.Redis, gRPC, etc.
///
/// Automatically filters out Stove's own dashboard gRPC traffic.
/// </summary>
public sealed class InProcessTraceCollector : ITraceCollector
{
    /// <summary>Default ActivitySource names to monitor.</summary>
    public static readonly string[] DefaultSources =
    [
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "MongoDB.Driver.Core.Extensions.DiagnosticSources",
        "StackExchange.Redis",
        "Grpc.Net.Client"
    ];

    private readonly Func<string, bool> _sourceFilter;
    private ActivityListener? _listener;
    private IStoveEventEmitter? _emitter;

    // Track trace IDs that belong to dashboard gRPC traffic.
    // Once any span in a trace is identified as dashboard traffic,
    // all spans sharing that trace ID are suppressed.
    private readonly ConcurrentDictionary<string, bool> _dashboardTraceIds = new();

    /// <summary>
    /// Create a collector that monitors the default set of ActivitySource names.
    /// </summary>
    public InProcessTraceCollector()
        : this(name => DefaultSources.Any(s => name.StartsWith(s, StringComparison.Ordinal)))
    {
    }

    /// <summary>
    /// Create a collector with a custom source filter predicate.
    /// The predicate receives the ActivitySource.Name and returns true to capture.
    /// </summary>
    public InProcessTraceCollector(Func<string, bool> sourceFilter)
    {
        _sourceFilter = sourceFilter;
    }

    public void Start(IStoveEventEmitter emitter)
    {
        _emitter = emitter;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => _sourceFilter(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Stop()
    {
        _listener?.Dispose();
        _listener = null;
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    private void OnActivityStopped(Activity activity)
    {
        if (_emitter == null) return;

        var traceId = activity.TraceId.ToString();

        // If this trace was already flagged as dashboard traffic, skip
        if (_dashboardTraceIds.ContainsKey(traceId))
            return;

        // Check if this specific activity is dashboard traffic
        if (IsDashboardActivity(activity))
        {
            _dashboardTraceIds.TryAdd(traceId, true);
            return;
        }

        var span = MapActivityToSpan(activity);
        _emitter.EmitSpan(span);
    }

    /// <summary>
    /// Returns true if this specific activity is a dashboard gRPC/HTTP call.
    /// Checks URL tags and gRPC service tags for the dashboard endpoint.
    /// </summary>
    private static bool IsDashboardActivity(Activity activity)
    {
        foreach (var tag in activity.Tags)
        {
            // HTTP spans targeting the dashboard endpoint
            if (tag.Key is "url.full" or "http.url" &&
                tag.Value?.Contains("4041") == true)
                return true;

            // gRPC service name
            if (tag.Key == "rpc.service" &&
                tag.Value?.Contains("Dashboard", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            // server.port as separate tag
            if (tag.Key == "server.port" && tag.Value == "4041")
                return true;
        }

        return false;
    }

    internal static StoveSpan MapActivityToSpan(Activity activity)
    {
        var attributes = new Dictionary<string, string>();
        foreach (var tag in activity.Tags)
        {
            if (tag.Value != null)
                attributes[tag.Key] = tag.Value;
        }

        StoveExceptionInfo? exception = null;
        var exEvents = activity.Events.Where(e =>
            e.Name == "exception").ToList();
        if (exEvents.Count > 0)
        {
            var exEvent = exEvents[0];
            var exType = exEvent.Tags.FirstOrDefault(t => t.Key == "exception.type").Value?.ToString() ?? "Exception";
            var exMsg = exEvent.Tags.FirstOrDefault(t => t.Key == "exception.message").Value?.ToString() ?? "";
            var exStack = exEvent.Tags.FirstOrDefault(t => t.Key == "exception.stacktrace").Value?.ToString() ?? "";
            exception = new StoveExceptionInfo(exType, exMsg, exStack.Split('\n'));
        }

        var status = activity.Status == ActivityStatusCode.Error ? "error" : "ok";

        return new StoveSpan
        {
            TraceId = activity.TraceId.ToString(),
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = activity.ParentSpanId.ToString(),
            OperationName = activity.DisplayName,
            ServiceName = activity.Source.Name,
            Start = activity.StartTimeUtc,
            End = activity.StartTimeUtc + activity.Duration,
            Status = status,
            Attributes = attributes,
            Exception = exception
        };
    }
}
