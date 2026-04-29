using System.Text;

namespace Stove.Net.Core;

/// <summary>
/// Renders a SpanTree as an ASCII tree for console output.
/// Matches Kotlin Stove's TraceTreeRenderer functionality.
/// </summary>
public static class SpanTreeRenderer
{
    /// <summary>
    /// Render the span tree as an ASCII string suitable for console output.
    /// Example output:
    /// <code>
    /// ✅ Validate: Should_create_order (234ms)
    /// ├── ✅ Http: POST /api/orders (145ms)
    /// │   ├── 🔍 Microsoft.AspNetCore: POST /api/orders (142ms)
    /// │   │   ├── 🔍 Npgsql: INSERT INTO orders (32ms)
    /// │   │   └── 🔍 System.Net.Http: POST http://localhost/api/notifications (12ms)
    /// │   └── ✅ PostgreSql: ShouldQuery (18ms)
    /// └── ✅ Redis: GetAsync (5ms)
    /// </code>
    /// </summary>
    public static string RenderAscii(SpanTree tree)
    {
        if (tree.Root == null) return "(empty trace)";

        var sb = new StringBuilder();
        RenderNode(sb, tree.Root, "", true);

        sb.AppendLine();
        sb.Append($"  {tree.TotalSpans} span(s)");
        if (tree.FailedSpans > 0)
            sb.Append($", {tree.FailedSpans} failed");

        return sb.ToString();
    }

    private static void RenderNode(StringBuilder sb, SpanNode node, string indent, bool isLast)
    {
        var icon = node.Span.Status == "error" ? "❌" : "✅";
        var serviceName = node.Span.ServiceName;
        var opName = node.Span.OperationName;
        var durationMs = node.Span.DurationMs;

        // Use a different icon for server-side/infrastructure spans
        if (IsInfraSpan(serviceName))
            icon = "🔍";

        if (sb.Length > 0)
        {
            sb.AppendLine();
            sb.Append(indent);
            sb.Append(isLast ? "└── " : "├── ");
        }

        sb.Append($"{icon} {serviceName}: {opName} ({durationMs}ms)");

        if (node.Span.Exception is { } ex)
            sb.Append($" [{ex.Type}: {ex.Message}]");

        var childIndent = indent + (isLast ? "    " : "│   ");
        for (var i = 0; i < node.Children.Count; i++)
            RenderNode(sb, node.Children[i], childIndent, i == node.Children.Count - 1);
    }

    private static bool IsInfraSpan(string serviceName)
    {
        return serviceName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               serviceName.StartsWith("System.", StringComparison.Ordinal) ||
               serviceName.StartsWith("Npgsql", StringComparison.Ordinal) ||
               serviceName.StartsWith("StackExchange.", StringComparison.Ordinal) ||
               serviceName.StartsWith("MongoDB.", StringComparison.Ordinal) ||
               serviceName.StartsWith("Grpc.", StringComparison.Ordinal);
    }
}
