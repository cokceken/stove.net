namespace Stove.Net.Core;

/// <summary>
/// Emitter interface exposed to individual systems so they can record actions and spans.
/// The implementation fans out to all registered IStoveEventListeners.
/// </summary>
public interface IStoveEventEmitter
{
    /// <summary>Current test ID. Empty string when no test is active.</summary>
    string CurrentTestId { get; }

    /// <summary>Current trace ID set by Validate(). Empty when no trace is active.</summary>
    string CurrentTraceId { get; }

    /// <summary>Current parent span ID. Systems use this as their parent when emitting child spans.</summary>
    string CurrentSpanId { get; }

    /// <summary>Record a DSL action entry.</summary>
    void Emit(StoveEntry entry);

    /// <summary>Record a timed span.</summary>
    void EmitSpan(StoveSpan span);

    /// <summary>Record a state snapshot (e.g., HTTP body, DB state).</summary>
    void EmitSnapshot(StoveSnapshot snapshot);
}
