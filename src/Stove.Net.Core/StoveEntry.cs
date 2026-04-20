namespace Stove.Net.Core;

/// <summary>
/// Represents a single recorded action performed by a Stove system during a test.
/// Maps to EntryRecordedEvent in the Stove dashboard protocol.
/// </summary>
public sealed record StoveEntry
{
    public string TestId { get; init; } = string.Empty;
    public string System { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public EntryResult Result { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }
    public string? Expected { get; init; }
    public string? Actual { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsSuccess => Result == EntryResult.Success;
    public bool IsFailed => Result == EntryResult.Failed;
}

public enum EntryResult
{
    Success,
    Failed
}
