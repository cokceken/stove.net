namespace Stove.Net.Core;

/// <summary>
/// Host-agnostic contract for capturing server-side traces.
/// Implementations collect trace spans from the system under test and emit them
/// into the Stove event bus as StoveSpan records.
///
/// Built-in implementations:
/// - InProcessTraceCollector: uses System.Diagnostics.ActivityListener (for WebApplicationFactory)
/// Future:
/// - OtlpTraceCollector: starts an OTLP gRPC endpoint (for Docker-hosted SUTs)
/// </summary>
public interface ITraceCollector : IAsyncDisposable
{
    /// <summary>
    /// Start collecting traces and emitting them to the given emitter.
    /// Called during RunSystemsAsync.
    /// </summary>
    void Start(IStoveEventEmitter emitter);

    /// <summary>
    /// Stop collecting traces. Called during DisposeAsync.
    /// </summary>
    void Stop();
}
