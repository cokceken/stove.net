namespace Stove.Net.Core.Reporting;

/// <summary>
/// Represents a point-in-time state snapshot captured during a test.
/// Maps to SnapshotEvent in the Stove dashboard protocol.
/// Used for HTTP request/response body capture, database state, etc.
/// </summary>
public sealed record StoveSnapshot
{
    public string TestId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string System { get; init; } = string.Empty;
    public string StateJson { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
