using System.Collections.Concurrent;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Tracing;

/// <summary>
/// Thread-safe storage for collected OTLP spans with traceId→testId correlation.
/// Spans arrive from the OtlpReceiver and are queryable by trace ID or test ID.
/// </summary>
public sealed class StoveTraceCollector
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<StoveSpan>> _spans = new();
    private readonly ConcurrentDictionary<string, string> _traceToTest = new();
    private readonly int _maxSpansPerTrace;

    private Action<StoveSpan>? _onSpanRecorded;

    public StoveTraceCollector(int maxSpansPerTrace = 1000)
    {
        _maxSpansPerTrace = maxSpansPerTrace;
    }

    internal void SetSpanCallback(Action<StoveSpan> callback) => _onSpanRecorded = callback;

    /// <summary>
    /// Register a trace-to-test mapping. Call before the test step starts
    /// so incoming spans can be correlated.
    /// </summary>
    public void RegisterTrace(string traceId, string testId)
    {
        _traceToTest[traceId] = testId;
        _spans.TryAdd(traceId, new ConcurrentBag<StoveSpan>());
    }

    /// <summary>Record a span received from the OTLP receiver.</summary>
    public void RecordSpan(StoveSpan span)
    {
        // Only store and emit spans for traces we're tracking (registered via RegisterTrace)
        if (!_traceToTest.ContainsKey(span.TraceId))
            return;

        var bag = _spans.GetOrAdd(span.TraceId, _ => new ConcurrentBag<StoveSpan>());
        if (bag.Count < _maxSpansPerTrace)
        {
            bag.Add(span);
            _onSpanRecorded?.Invoke(span);
        }
    }

    /// <summary>Get all spans for a given trace ID.</summary>
    public IReadOnlyList<StoveSpan> GetSpansForTrace(string traceId)
        => _spans.TryGetValue(traceId, out var bag)
            ? bag.ToList()
            : [];

    /// <summary>Get the test ID associated with a trace ID.</summary>
    public string? GetTestId(string traceId)
        => _traceToTest.TryGetValue(traceId, out var testId) ? testId : null;

    /// <summary>Get all trace IDs associated with a test.</summary>
    public IReadOnlyList<string> GetTracesForTest(string testId)
        => _traceToTest
            .Where(kv => kv.Value == testId)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>Get spans with ERROR status for a given trace.</summary>
    public IReadOnlyList<StoveSpan> GetFailedSpans(string traceId)
        => GetSpansForTrace(traceId)
            .Where(s => s.Status == "ERROR")
            .ToList();

    /// <summary>Check if any spans in the trace have ERROR status.</summary>
    public bool HasFailures(string traceId)
        => GetSpansForTrace(traceId).Any(s => s.Status == "ERROR");

    /// <summary>Number of spans for a trace.</summary>
    public int SpanCount(string traceId)
        => _spans.TryGetValue(traceId, out var bag) ? bag.Count : 0;

    /// <summary>Total spans across all traces.</summary>
    public int TotalSpanCount()
        => _spans.Values.Sum(bag => bag.Count);

    /// <summary>Number of unique traces.</summary>
    public int TraceCount() => _spans.Count;

    /// <summary>
    /// Poll for spans to arrive for the given trace, with intelligent two-phase waiting.
    /// Phase 1: Poll until first spans arrive (or timeout).
    /// Phase 2: Wait for straggler spans after the first arrive.
    /// </summary>
    public async Task<IReadOnlyList<StoveSpan>> WaitForSpansAsync(
        string traceId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        TimeSpan stragglerWait)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        // Phase 1: Poll until first spans arrive
        while (DateTimeOffset.UtcNow < deadline)
        {
            var spans = GetSpansForTrace(traceId);
            if (spans.Count > 0)
            {
                // Phase 2: Wait for straggler spans
                var remaining = deadline - DateTimeOffset.UtcNow;
                var wait = remaining < stragglerWait ? remaining : stragglerWait;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait);
                return GetSpansForTrace(traceId);
            }

            await Task.Delay(pollInterval);
        }

        return GetSpansForTrace(traceId);
    }

    /// <summary>
    /// Fallback: search for a trace that contains the test ID in span attributes.
    /// Used when the primary traceId has no spans (e.g., instrumentation rewrote traceparent).
    /// </summary>
    public string? FindTraceByTestIdAttribute(string testId)
    {
        foreach (var (traceId, bag) in _spans)
        {
            foreach (var span in bag)
            {
                if (span.Attributes.TryGetValue("stove.test.id", out var val) && val == testId)
                    return traceId;
                if (span.Attributes.TryGetValue("X-Stove-Test-Id", out val) && val == testId)
                    return traceId;
            }
        }

        return null;
    }

    /// <summary>Clear all stored spans and mappings.</summary>
    public void ClearAll()
    {
        _spans.Clear();
        _traceToTest.Clear();
    }

    /// <summary>Clear spans for a specific trace.</summary>
    public void Clear(string traceId)
    {
        _spans.TryRemove(traceId, out _);
        _traceToTest.TryRemove(traceId, out _);
    }
}
