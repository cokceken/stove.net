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
/// Renders a formatted console report for failed tests.
/// Inspired by Kotlin Stove's PrettyConsoleRenderer.
/// </summary>
public static class ConsoleReportRenderer
{
    private const int BoxWidth = 78;
    private const int MaxFieldLength = 500;
    private const string ContainerPrefix = "Container:";

    /// <summary>
    /// Render a failure report for a single test.
    /// Returns the formatted string (caller decides where to write it).
    /// </summary>
    public static string Render(TestReport report)
    {
        var sb = new StringBuilder();

        RenderHeader(sb);
        RenderTestBox(sb, report);

        return sb.ToString();
    }

    private static void RenderHeader(StringBuilder sb)
    {
        var rule = new string('═', BoxWidth);
        sb.AppendLine(rule);
        sb.AppendLine(CenterText("STOVE EXECUTION REPORT", BoxWidth));
        sb.AppendLine(rule);
        sb.AppendLine();
    }

    private static void RenderTestBox(StringBuilder sb, TestReport report)
    {
        var topBorder = "╔" + new string('═', BoxWidth) + "╗";
        var separator = "╠" + new string('═', BoxWidth) + "╣";
        var bottomBorder = "╚" + new string('═', BoxWidth) + "╝";

        sb.AppendLine(topBorder);
        RenderBoxLine(sb, $"Test: {report.SpecName}::{report.TestName}");
        RenderBoxLine(sb, $"Duration: {report.Duration.TotalSeconds:F3}s");

        if (!string.IsNullOrEmpty(report.Error))
        {
            RenderBoxLine(sb, $"Error: {Truncate(report.Error, BoxWidth - 10)}");
        }

        // Timeline
        var timelineEntries = report.Entries
            .Where(e => !e.System.StartsWith(ContainerPrefix, StringComparison.Ordinal))
            .OrderBy(e => e.Timestamp)
            .ToList();

        if (timelineEntries.Count > 0)
        {
            sb.AppendLine(separator);
            RenderEmptyBoxLine(sb);
            RenderBoxLine(sb, "── Timeline ──");
            RenderEmptyBoxLine(sb);

            foreach (var entry in timelineEntries)
            {
                RenderTimelineEntry(sb, entry);
            }
        }

        // Snapshots
        if (report.Snapshots.Count > 0)
        {
            sb.AppendLine(separator);
            RenderEmptyBoxLine(sb);
            RenderBoxLine(sb, "── System Snapshots ──");
            RenderEmptyBoxLine(sb);

            foreach (var snapshot in report.Snapshots)
            {
                RenderSnapshot(sb, snapshot);
            }
        }

        // Application Logs
        if (report.ApplicationLogs.Count > 0)
        {
            sb.AppendLine(separator);
            RenderEmptyBoxLine(sb);
            RenderBoxLine(sb, "── Application Logs ──");
            RenderEmptyBoxLine(sb);

            foreach (var log in report.ApplicationLogs)
            {
                var time = log.Timestamp.ToString("HH:mm:ss.fff");
                var level = MapLogLevel(log.Level);
                RenderBoxLine(sb, $"{time} [{level}] {log.Category}: {log.Message}");
            }

            RenderEmptyBoxLine(sb);
        }

        // Container Logs
        if (report.ContainerLogs.Count > 0)
        {
            sb.AppendLine(separator);
            RenderEmptyBoxLine(sb);
            RenderBoxLine(sb, "── Container Logs ──");
            RenderEmptyBoxLine(sb);

            foreach (var log in report.ContainerLogs)
            {
                var time = log.Timestamp.ToString("HH:mm:ss.fff");
                RenderBoxLine(sb, $"[{log.Source}] {time} {Truncate(log.Message, BoxWidth - log.Source.Length - 18)}");
            }

            RenderEmptyBoxLine(sb);
        }

        sb.AppendLine(bottomBorder);
    }

    private static void RenderTimelineEntry(StringBuilder sb, StoveEntry entry)
    {
        var time = entry.Timestamp.ToString("HH:mm:ss.fff");
        var marker = entry.IsSuccess ? "✓" : "✗";

        RenderBoxLine(sb, $"{time} {marker} [{entry.System}] {entry.Action}");

        if (!string.IsNullOrEmpty(entry.Input))
            RenderBoxLine(sb, $"    Input: {Truncate(entry.Input, MaxFieldLength)}");

        if (!string.IsNullOrEmpty(entry.Output))
            RenderBoxLine(sb, $"    Output: {Truncate(entry.Output, MaxFieldLength)}");

        if (!string.IsNullOrEmpty(entry.Expected))
            RenderBoxLine(sb, $"    Expected: {Truncate(entry.Expected, MaxFieldLength)}");

        if (!string.IsNullOrEmpty(entry.Actual))
            RenderBoxLine(sb, $"    Actual: {Truncate(entry.Actual, MaxFieldLength)}");

        if (!string.IsNullOrEmpty(entry.Error))
            RenderBoxLine(sb, $"    Error: {Truncate(entry.Error, MaxFieldLength)}");

        RenderEmptyBoxLine(sb);
    }

    private static void RenderSnapshot(StringBuilder sb, StoveSnapshot snapshot)
    {
        RenderBoxLine(sb, $"┌─ {snapshot.System} " + new string('─', Math.Max(0, BoxWidth - snapshot.System.Length - 6)));

        if (!string.IsNullOrEmpty(snapshot.Summary))
            RenderBoxLine(sb, $"  {snapshot.Summary}");

        if (!string.IsNullOrEmpty(snapshot.StateJson))
            RenderBoxLine(sb, $"  State: {Truncate(snapshot.StateJson, MaxFieldLength)}");

        RenderEmptyBoxLine(sb);
    }

    private static void RenderBoxLine(StringBuilder sb, string content)
    {
        // Truncate content to fit within the box if needed
        var maxContent = BoxWidth - 4; // "║ " + content + " ║"
        var text = content.Length > maxContent
            ? content[..(maxContent - 3)] + "..."
            : content;

        sb.Append("║ ");
        sb.Append(text);
        sb.Append(new string(' ', Math.Max(0, BoxWidth - text.Length - 2)));
        sb.AppendLine("║");
    }

    private static void RenderEmptyBoxLine(StringBuilder sb)
    {
        sb.Append("║");
        sb.Append(new string(' ', BoxWidth));
        sb.AppendLine("║");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength < 4) maxLength = 4;

        // Replace newlines with spaces for single-line display
        var sanitized = value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..(maxLength - 3)] + "...";
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width) return text;
        var padding = (width - text.Length) / 2;
        return new string(' ', padding) + text;
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
