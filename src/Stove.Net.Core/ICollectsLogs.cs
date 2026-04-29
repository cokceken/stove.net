namespace Stove.Net.Core;

/// <summary>
/// Interface for systems that can provide container or service logs.
/// Typically implemented by Testcontainers-backed systems.
/// The xUnit test runner collects logs from all ICollectsLogs systems
/// at test end and emits them as StoveEntry events.
/// </summary>
public interface ICollectsLogs
{
    /// <summary>
    /// Returns log entries since the given timestamp.
    /// </summary>
    Task<IReadOnlyList<ContainerLogEntry>> GetLogsSinceAsync(DateTimeOffset since);
}

/// <summary>
/// A single log line from a container or service.
/// </summary>
public sealed record ContainerLogEntry(
    DateTimeOffset Timestamp,
    string Source,
    string ContainerId,
    string Message,
    string Stream);
