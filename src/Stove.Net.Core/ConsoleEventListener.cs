namespace Stove.Net.Core;

/// <summary>
/// Prints structured Stove events to the console (stdout).
/// Enable via StoveBuilder.WithConsoleReporter().
/// </summary>
public sealed class ConsoleEventListener : IStoveEventListener
{
    private const string Prefix = "[STOVE]";
    private const string Pass = "✅";
    private const string Fail = "❌";

    public void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems)
    {
        var systemList = systems.Count > 0 ? string.Join(", ", systems) : "none";
        Console.WriteLine($"{Prefix} Run started — systems: {systemList}");
    }

    public void OnTestStarted(string testId, string testName, string specName)
    {
        var label = string.IsNullOrEmpty(specName) ? testName : $"{specName} › {testName}";
        Console.WriteLine($"{Prefix} ▶ {label}");
    }

    public void OnEntryRecorded(StoveEntry entry)
    {
        var icon = entry.IsSuccess ? Pass : Fail;
        var location = $"{entry.System}.{entry.Action}";
        var detail = BuildDetail(entry);
        Console.WriteLine($"{Prefix} {icon} {location,-40} {detail}");
    }

    public void OnSpanRecorded(StoveSpan span)
    {
        // Root spans (Validate calls) are shown as scope markers
        if (string.IsNullOrEmpty(span.ParentSpanId))
        {
            var icon = span.Status == "ok" ? Pass : Fail;
            Console.WriteLine($"{Prefix}   {icon} {span.OperationName} ({span.DurationMs}ms)");
            return;
        }

        // Server-side captured spans (from ITraceCollector) — show key details
        if (span.ServiceName is "Validate" or "Http" or "PostgreSql" or "Redis"
            or "Kafka" or "WireMock" or "MongoDb")
            return; // Already shown as StoveEntry — skip duplicate

        var detail = BuildSpanDetail(span);
        if (!string.IsNullOrEmpty(detail))
            Console.WriteLine($"{Prefix}   🔍 {span.ServiceName,-25} {detail}  ({span.DurationMs}ms)");
    }

    public void OnTestEnded(string testId, TimeSpan duration, string? error)
    {
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

        if (!string.IsNullOrEmpty(entry.Input))
            parts.Add(Truncate(entry.Input, 80));

        if (!string.IsNullOrEmpty(entry.Output))
            parts.Add($"→ {Truncate(entry.Output, 60)}");

        if (entry.IsFailed && !string.IsNullOrEmpty(entry.Error))
            parts.Add($"[{Truncate(entry.Error, 120)}]");

        return parts.Count > 0 ? string.Join("  ", parts) : string.Empty;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

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
