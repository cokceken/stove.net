namespace Stove.Net.Core;

/// <summary>
/// Marker interface for all plugged systems (PostgreSQL, HTTP, Kafka, etc.).
/// Each system manages its own lifecycle and cleanup.
/// </summary>
public interface IPluggedSystem : IAsyncDisposable
{
    /// <summary>
    /// Start the system (e.g., spin up a Testcontainer, create an HTTP client).
    /// </summary>
    Task RunAsync();

    /// <summary>
    /// Clean up state between tests (e.g., truncate tables, clear queues).
    /// </summary>
    Task CleanupAsync();
}
