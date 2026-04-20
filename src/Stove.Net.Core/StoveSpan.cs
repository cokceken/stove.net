namespace Stove.Net.Core;

/// <summary>
/// Represents a timed span of execution within a Stove test trace.
/// Maps to SpanRecordedEvent in the Stove dashboard protocol.
/// Each Validate() call creates a root span; each system assertion creates a child span.
/// </summary>
public sealed record StoveSpan
{
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string ParentSpanId { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string Status { get; init; } = "ok";
    public Dictionary<string, string> Attributes { get; init; } = new();
    public StoveExceptionInfo? Exception { get; init; }

    public long DurationMs => (long)(End - Start).TotalMilliseconds;

    /// <summary>Generate a 16-char hex span ID.</summary>
    public static string NewSpanId() => Guid.NewGuid().ToString("N")[..16];
}

/// <summary>Exception information attached to a failed span.</summary>
public sealed record StoveExceptionInfo(string Type, string Message, string[] StackTrace);
