namespace Stove.Net.Core;

/// <summary>
/// Builds a hierarchical span tree from a flat list of StoveSpan records.
/// Matches Kotlin Stove's SpanTree/SpanNode/TraceVisualization functionality.
/// </summary>
public sealed class SpanTree
{
    public SpanNode? Root { get; }
    public int TotalSpans { get; }
    public int FailedSpans { get; }

    private SpanTree(SpanNode? root, int totalSpans, int failedSpans)
    {
        Root = root;
        TotalSpans = totalSpans;
        FailedSpans = failedSpans;
    }

    /// <summary>
    /// Build a span tree from a flat list of spans that share the same trace ID.
    /// </summary>
    public static SpanTree Build(IReadOnlyList<StoveSpan> spans)
    {
        if (spans.Count == 0)
            return new SpanTree(null, 0, 0);

        var byId = new Dictionary<string, StoveSpan>(spans.Count);
        var childMap = new Dictionary<string, List<StoveSpan>>();

        foreach (var span in spans)
        {
            byId[span.SpanId] = span;
            var parentId = span.ParentSpanId;
            if (!childMap.TryGetValue(parentId, out var siblings))
            {
                siblings = [];
                childMap[parentId] = siblings;
            }
            siblings.Add(span);
        }

        // Find root(s): spans whose ParentSpanId is empty or not in the set
        var roots = spans.Where(s =>
            string.IsNullOrEmpty(s.ParentSpanId) || !byId.ContainsKey(s.ParentSpanId)).ToList();

        var root = roots.Count > 0 ? roots.OrderBy(r => r.Start).First() : spans[0];
        var rootNode = BuildNode(root, childMap);

        var failedCount = spans.Count(s => s.Status == "error");
        return new SpanTree(rootNode, spans.Count, failedCount);
    }

    private static SpanNode BuildNode(StoveSpan span, Dictionary<string, List<StoveSpan>> childMap)
    {
        var children = new List<SpanNode>();
        if (childMap.TryGetValue(span.SpanId, out var childSpans))
        {
            foreach (var child in childSpans.OrderBy(c => c.Start))
                children.Add(BuildNode(child, childMap));
        }
        return new SpanNode(span, children);
    }
}

/// <summary>
/// A node in the span tree representing one operation and its children.
/// </summary>
public sealed class SpanNode
{
    public StoveSpan Span { get; }
    public IReadOnlyList<SpanNode> Children { get; }

    public SpanNode(StoveSpan span, IReadOnlyList<SpanNode> children)
    {
        Span = span;
        Children = children;
    }

    public bool HasFailedDescendants =>
        Span.Status == "error" || Children.Any(c => c.HasFailedDescendants);

    public long TotalDurationMs => Span.DurationMs;

    public int SpanCount => 1 + Children.Sum(c => c.SpanCount);

    /// <summary>Find the deepest failed span (useful for pinpointing root cause).</summary>
    public SpanNode? FindFailurePoint()
    {
        if (Span.Status == "error" && !Children.Any(c => c.HasFailedDescendants))
            return this;

        foreach (var child in Children)
        {
            var fp = child.FindFailurePoint();
            if (fp != null) return fp;
        }
        return Span.Status == "error" ? this : null;
    }
}
