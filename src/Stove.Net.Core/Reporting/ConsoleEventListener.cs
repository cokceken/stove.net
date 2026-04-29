namespace Stove.Net.Core.Reporting;

/// <summary>
/// Prints structured Stove events to the console (stdout).
/// Enable via StoveBuilder.WithConsoleReporter().
/// Collects spans per test and renders an ASCII span tree at test end.
/// </summary>
public sealed class ConsoleEventListener : IStoveEventListener
{
    private const string Prefix = "[STOVE]";
    private const string Pass = "✅";
    private const string Fail = "❌";

    // Buffer spans per trace for span tree rendering at test end
    private readonly Dictionary<string, List<StoveSpan>> _spansByTrace = new();
    private string _currentTraceId = string.Empty;

    public void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems)
    {
        var systemList = systems.Count > 0 ? string.Join(", ", systems) : "none";
        Console.WriteLine($"{Prefix} Run started — systems: {systemList}");
    }

    public void OnTestStarted(string testId, string testName, string specName, string[]? testPath = null)
    {
        string label;
        if (testPath is { Length: > 0 })
            label = string.Join(" › ", testPath);
        else if (!string.IsNullOrEmpty(specName))
            label = $"{specName} › {testName}";
        else
            label = testName;
        Console.WriteLine($"{Prefix} ▶ {label}");
    }

    public void OnEntryRecorded(StoveEntry entry)
    {
        // Application logs get a distinct rendering
        if (entry.System == "Application")
        {
            var levelIcon = GetLogLevelIcon(entry.Action);
            var category = entry.Input ?? "Unknown";
            var message = entry.Output ?? "";
            Console.WriteLine($"{Prefix} {levelIcon} [{category}] {message}");
            if (!string.IsNullOrEmpty(entry.Error))
                Console.WriteLine($"{Prefix}       {Truncate(entry.Error, 200)}");
            return;
        }

        var icon = entry.IsSuccess ? Pass : Fail;
        var location = $"{entry.System}.{entry.Action}";
        var detail = BuildDetail(entry);
        Console.WriteLine($"{Prefix} {icon} {location,-40} {detail}");
    }

    public void OnSpanRecorded(StoveSpan span)
    {
        // Buffer span for tree rendering at test end
        if (!string.IsNullOrEmpty(span.TraceId))
        {
            if (!_spansByTrace.TryGetValue(span.TraceId, out var spans))
            {
                spans = [];
                _spansByTrace[span.TraceId] = spans;
            }
            spans.Add(span);
            if (string.IsNullOrEmpty(_currentTraceId))
                _currentTraceId = span.TraceId;
        }

        // Root spans (Validate calls) are shown as scope markers
        if (string.IsNullOrEmpty(span.ParentSpanId))
        {
            var icon = span.Status == "OK" ? Pass : Fail;
            Console.WriteLine($"{Prefix}   {icon} {span.OperationName} ({span.DurationMs}ms)");
            return;
        }

        // Server-side captured spans (from ITraceCollector) — show key details
        // Log spans are already shown via OnEntryRecorded — skip inline display
        if (span.ServiceName is "Validate" or "Http" or "PostgreSql" or "Redis"
            or "Kafka" or "WireMock" or "MongoDb"
            || span.ServiceName.StartsWith("Log.", StringComparison.Ordinal))
            return; // Already shown as StoveEntry — skip duplicate

        var detail = BuildSpanDetail(span);
        if (!string.IsNullOrEmpty(detail))
            Console.WriteLine($"{Prefix}   🔍 {span.ServiceName,-25} {detail}  ({span.DurationMs}ms)");
    }

    public void OnSnapshotRecorded(StoveSnapshot snapshot)
    {
        var summary = string.IsNullOrEmpty(snapshot.Summary)
            ? Truncate(snapshot.StateJson, 120)
            : snapshot.Summary;
        Console.WriteLine($"{Prefix}   📸 {snapshot.System,-25} {summary}");
    }

    public void OnTestEnded(string testId, TimeSpan duration, string? error)
    {
        // Render span tree if we have spans for this test's trace
        if (!string.IsNullOrEmpty(_currentTraceId) &&
            _spansByTrace.TryGetValue(_currentTraceId, out var spans) && spans.Count > 1)
        {
            var tree = SpanTree.Build(spans);
            var ascii = SpanTreeRenderer.RenderAscii(tree);
            Console.WriteLine($"{Prefix} 🌳 Trace:");
            foreach (var line in ascii.Split('\n'))
                Console.WriteLine($"{Prefix}   {line}");
        }

        // Clean up trace buffer
        if (!string.IsNullOrEmpty(_currentTraceId))
            _spansByTrace.Remove(_currentTraceId);
        _currentTraceId = string.Empty;

        if (error != null)
            Console.WriteLine($"{Prefix} {Fail} Test failed in {duration.TotalMilliseconds:F0}ms: {error}");
        else
            Console.WriteLine($"{Prefix} {Pass} Test passed in {duration.TotalMilliseconds:F0}ms");
    }

    public void OnRunEnded(int total, int passed, int failed, TimeSpan duration)
    {
        Console.WriteLine($"{Prefix} Run ended — {passed}/{total} passed in {duration.TotalSeconds:F1}s");
    }

    private static string BuildDetail(StoveEntry entry)
    {
        var parts = new List<string>();

        // Show key metadata inline when available
        if (entry.Metadata is { Count: > 0 })
        {
            if (entry.Metadata.TryGetValue("http.method", out var method) &&
                entry.Metadata.TryGetValue("http.url", out var url))
            {
                var status = entry.Metadata.GetValueOrDefault("http.status_code", "");
                parts.Add($"{method} {url} [{status}]".TrimEnd());
            }
            else if (entry.Metadata.TryGetValue("db.statement", out var sql))
            {
                parts.Add(Truncate(sql, 80));
            }
            else if (entry.Metadata.TryGetValue("messaging.destination", out var topic))
            {
                var key = entry.Metadata.GetValueOrDefault("messaging.kafka.message.key", "");
                parts.Add(string.IsNullOrEmpty(key) ? topic : $"{topic}:{key}");
            }
            else if (entry.Metadata.TryGetValue("db.collection", out var coll))
            {
                parts.Add(coll);
            }
            else if (entry.Metadata.TryGetValue("redis.key", out var redisKey))
            {
                parts.Add(redisKey);
            }
        }

        if (parts.Count == 0 && !string.IsNullOrEmpty(entry.Input))
            parts.Add(Truncate(entry.Input, 80));

        if (!string.IsNullOrEmpty(entry.Output))
            parts.Add($"→ {Truncate(entry.Output, 60)}");

        if (entry.IsFailed && !string.IsNullOrEmpty(entry.Error))
            parts.Add($"[{Truncate(entry.Error, 120)}]");

        return parts.Count > 0 ? string.Join("  ", parts) : string.Empty;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string GetLogLevelIcon(string logLevel) => logLevel switch
    {
        "Trace" => "⬜",
        "Debug" => "🔹",
        "Information" => "ℹ️",
        "Warning" => "⚠️",
        "Error" => "🔴",
        "Critical" => "💥",
        _ => "📝"
    };

    private static string BuildSpanDetail(StoveSpan span)
    {
        var attrs = span.Attributes;

        // HTTP spans (ASP.NET Core incoming or HttpClient outgoing)
        if (attrs.TryGetValue("http.request.method", out var method) ||
            attrs.TryGetValue("http.method", out method))
        {
            var route = attrs.GetValueOrDefault("http.route")
                        ?? attrs.GetValueOrDefault("url.full")
                        ?? attrs.GetValueOrDefault("http.url")
                        ?? span.OperationName;
            var status = attrs.GetValueOrDefault("http.response.status_code")
                         ?? attrs.GetValueOrDefault("http.status_code") ?? "";
            return $"{method} {Truncate(route, 60)} [{status}]";
        }

        // Database spans
        if (attrs.TryGetValue("db.statement", out var sql))
            return Truncate(sql, 100);

        if (attrs.TryGetValue("db.system", out var dbSystem))
            return $"{dbSystem}: {span.OperationName}";

        return span.OperationName;
    }
}
