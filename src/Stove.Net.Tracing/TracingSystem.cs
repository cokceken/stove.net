using Stove.Net.Core;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Tracing;

/// <summary>
/// Plugged system that spins up an OTLP gRPC receiver during RunAsync().
/// Exposes OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_EXPORTER_OTLP_PROTOCOL as configuration
/// so the application under test can export traces to Stove's collector.
///
/// Modeled after Kotlin Stove's TracingSystem (PluggedSystem + RunAware).
/// </summary>
public sealed class TracingSystem : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem, ITraceContextAware
{
    private readonly TracingSystemOptions _options;
    private readonly StoveTraceCollector _collector;
    private OtlpReceiver? _receiver;
    private IStoveEventEmitter? _emitter;

    public TracingSystem(TracingSystemOptions? options = null)
    {
        _options = options ?? new TracingSystemOptions();
        _collector = new StoveTraceCollector(_options.MaxSpansPerTrace);
    }

    /// <summary>Access the trace collector for query/assertion.</summary>
    public StoveTraceCollector Collector => _collector;

    /// <summary>The OTLP endpoint. Available after RunAsync().</summary>
    public string? Endpoint => _receiver?.Endpoint;

    /// <inheritdoc />
    public void SetEmitter(IStoveEventEmitter emitter)
    {
        _emitter = emitter;
        _collector.SetSpanCallback(span =>
        {
            _emitter?.EmitSpan(span);
        });
    }

    public async Task RunAsync()
    {
        _receiver = new OtlpReceiver(_collector, _options.Port);
        await _receiver.StartAsync();

        // Set a short batch export delay so spans are sent quickly during tests
        // (default is 5000ms which causes spans to arrive after test completion)
        Environment.SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY",
            _options.BatchExportDelayMs.ToString());

        // Also set the OTLP endpoint env var so AddOtlpExporter() picks it up
        // automatically — this is the most reliable way regardless of how the
        // app configures its OTel options pipeline.
        if (Endpoint != null)
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", Endpoint);
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
        }
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (_options.ConfigureExposedConfiguration != null && Endpoint != null)
            return _options.ConfigureExposedConfiguration(Endpoint);

        if (Endpoint == null)
            return [];

        return
        [
            new("OTEL_EXPORTER_OTLP_ENDPOINT", Endpoint),
            new("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
        ];
    }

    public Task CleanupAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public void OnTraceStarted(string traceId, string testId)
        => _collector.RegisterTrace(traceId, testId);

    // --- Assertion helpers ---

    /// <summary>
    /// Wait for spans on a given trace and assert at least one matches the predicate.
    /// </summary>
    public async Task<IReadOnlyList<StoveSpan>> ShouldContainSpan(
        string traceId, Func<StoveSpan, bool> predicate, string? description = null)
    {
        var spans = await _collector.WaitForSpansAsync(
            traceId, _options.SpanCollectionTimeout,
            _options.SpanPollInterval, _options.StragglerWaitTime);

        if (!spans.Any(predicate))
        {
            var spanSummary = string.Join("\n",
                spans.Select(s => $"  [{s.Status}] {s.ServiceName}/{s.OperationName}"));
            throw new InvalidOperationException(
                $"Expected trace {traceId} to contain a span matching '{description ?? "predicate"}' " +
                $"but found {spans.Count} span(s):\n{spanSummary}");
        }

        return spans;
    }

    /// <summary>Assert no spans have ERROR status for the given trace.</summary>
    public async Task ShouldNotHaveFailedSpans(string traceId)
    {
        var spans = await _collector.WaitForSpansAsync(
            traceId, _options.SpanCollectionTimeout,
            _options.SpanPollInterval, _options.StragglerWaitTime);

        var failed = spans.Where(s => s.Status == "ERROR").ToList();
        if (failed.Count > 0)
        {
            var summary = string.Join("\n", failed.Select(s =>
                $"  [{s.ServiceName}/{s.OperationName}] {s.Exception?.Message ?? "no message"}"));
            throw new InvalidOperationException(
                $"Expected no failed spans in trace {traceId} but found {failed.Count}:\n{summary}");
        }
    }

    /// <summary>Render a tree-formatted representation of spans for debugging.</summary>
    public async Task<string> RenderTraceTree(string traceId)
    {
        var spans = await _collector.WaitForSpansAsync(
            traceId, _options.SpanCollectionTimeout,
            _options.SpanPollInterval, _options.StragglerWaitTime);

        if (spans.Count == 0) return $"(no spans for trace {traceId})";

        var byParent = spans.GroupBy(s => s.ParentSpanId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Start).ToList());
        var lines = new List<string>();

        void Render(string parentId, int depth)
        {
            if (!byParent.TryGetValue(parentId, out var children)) return;
            foreach (var span in children)
            {
                var indent = new string(' ', depth * 2);
                var status = span.Status == "ERROR" ? "❌" : "✓";
                lines.Add(
                    $"{indent}{status} {span.ServiceName}/{span.OperationName} [{span.DurationMs}ms]");
                Render(span.SpanId, depth + 1);
            }
        }

        Render(string.Empty, 0);

        // Also render any orphan roots (parentSpanId not in span set)
        var knownIds = new HashSet<string>(spans.Select(s => s.SpanId));
        foreach (var group in byParent.Where(g =>
                     g.Key != string.Empty && !knownIds.Contains(g.Key)))
        {
            foreach (var span in group.Value)
            {
                var status = span.Status == "ERROR" ? "❌" : "✓";
                lines.Add($"{status} {span.ServiceName}/{span.OperationName} [{span.DurationMs}ms]");
                Render(span.SpanId, 1);
            }
        }

        return string.Join("\n", lines);
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up env vars set during RunAsync
        Environment.SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY", null);
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", null);

        if (_receiver != null)
            await _receiver.DisposeAsync();
    }
}
