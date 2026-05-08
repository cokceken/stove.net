using System.Text;

namespace Stove.Net.Core.Reporting;

/// <summary>
/// A single captured application log line.
/// </summary>
public sealed record LogLine(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message);

/// <summary>
/// Aggregated data for a single test's report.
/// </summary>
public sealed record TestReport
{
    public required string TestId { get; init; }
    public required string TestName { get; init; }
    public required string SpecName { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<StoveEntry> Entries { get; init; } = [];
    public IReadOnlyList<StoveSnapshot> Snapshots { get; init; } = [];
    public IReadOnlyList<LogLine> ApplicationLogs { get; init; } = [];
    public IReadOnlyList<ContainerLogEntry> ContainerLogs { get; init; } = [];
}

/// <summary>
/// Renders a plain-text structured report for failed tests.
/// No ASCII box-drawing — just clear sections with full, untruncated data.
/// </summary>
public static class ConsoleReportRenderer
{
    private const string ContainerPrefix = "Container:";
    private const string Separator = "---";

    /// <summary>
    /// Render a failure report for a single test.
    /// Returns the formatted string (caller decides where to write it).
    /// </summary>
    public static string Render(TestReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("=== STOVE EXECUTION REPORT ===");
        sb.AppendLine();
        sb.AppendLine($"Test: {report.SpecName}::{report.TestName}");
        sb.AppendLine($"Duration: {report.Duration.TotalSeconds:F3}s");

        if (!string.IsNullOrEmpty(report.Error))
            sb.AppendLine($"Error: {report.Error}");

        // Timeline
        var timelineEntries = report.Entries
            .Where(e => !e.System.StartsWith(ContainerPrefix, StringComparison.Ordinal))
            .OrderBy(e => e.Timestamp)
            .ToList();

        if (timelineEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Separator);
            sb.AppendLine("Timeline:");
            sb.AppendLine(Separator);

            foreach (var entry in timelineEntries)
                RenderTimelineEntry(sb, entry);
        }

        // Snapshots
        if (report.Snapshots.Count > 0)
        {
            sb.AppendLine(Separator);
            sb.AppendLine("System Snapshots:");
            sb.AppendLine(Separator);

            foreach (var snapshot in report.Snapshots)
                RenderSnapshot(sb, snapshot);
        }

        // Application Logs
        if (report.ApplicationLogs.Count > 0)
        {
            sb.AppendLine(Separator);
            sb.AppendLine("Application Logs:");
            sb.AppendLine(Separator);

            foreach (var log in report.ApplicationLogs)
            {
                var time = log.Timestamp.ToString("HH:mm:ss.fff");
                var level = MapLogLevel(log.Level);
                sb.AppendLine($"  {time} [{level}] {log.Category}: {log.Message}");
            }
        }

        // Container Logs
        if (report.ContainerLogs.Count > 0)
        {
            sb.AppendLine(Separator);
            sb.AppendLine("Container Logs:");
            sb.AppendLine(Separator);

            foreach (var log in report.ContainerLogs)
            {
                var time = log.Timestamp.ToString("HH:mm:ss.fff");
                sb.AppendLine($"  [{log.Source}] {time} {log.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== END STOVE REPORT ===");

        return sb.ToString();
    }

    private static void RenderTimelineEntry(StringBuilder sb, StoveEntry entry)
    {
        var time = entry.Timestamp.ToString("HH:mm:ss.fff");
        var marker = entry.IsSuccess ? "OK" : "FAIL";

        sb.AppendLine($"  {time} [{marker}] [{entry.System}] {entry.Action}");

        if (!string.IsNullOrEmpty(entry.Input))
            sb.AppendLine($"    Input: {entry.Input}");

        if (!string.IsNullOrEmpty(entry.Output))
            sb.AppendLine($"    Output: {entry.Output}");

        if (!string.IsNullOrEmpty(entry.Expected))
            sb.AppendLine($"    Expected: {entry.Expected}");

        if (!string.IsNullOrEmpty(entry.Actual))
            sb.AppendLine($"    Actual: {entry.Actual}");

        if (!string.IsNullOrEmpty(entry.Error))
            sb.AppendLine($"    Error: {entry.Error}");
    }

    private static void RenderSnapshot(StringBuilder sb, StoveSnapshot snapshot)
    {
        sb.AppendLine($"  [{snapshot.System}]");

        if (!string.IsNullOrEmpty(snapshot.Summary))
            sb.AppendLine($"    {snapshot.Summary}");

        if (!string.IsNullOrEmpty(snapshot.StateJson))
            sb.AppendLine($"    State: {snapshot.StateJson}");
    }

    private static string MapLogLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "TRACE" or "TRC" => "TRC",
            "DEBUG" or "DBG" => "DBG",
            "INFORMATION" or "INF" or "INFO" => "INF",
            "WARNING" or "WRN" or "WARN" => "WRN",
            "ERROR" or "ERR" => "ERR",
            "CRITICAL" or "CRI" or "FTL" or "FATAL" => "FTL",
            _ => level.Length > 3 ? level[..3].ToUpperInvariant() : level.ToUpperInvariant()
        };
    }
}