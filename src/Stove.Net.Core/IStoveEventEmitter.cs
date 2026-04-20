namespace Stove.Net.Core;

/// <summary>
/// Emitter interface exposed to individual systems so they can record actions.
/// The implementation fans out to all registered IStoveEventListeners.
/// </summary>
public interface IStoveEventEmitter
{
    /// <summary>Current test ID. Empty string when no test is active.</summary>
    string CurrentTestId { get; }

    /// <summary>Record a DSL action entry.</summary>
    void Emit(StoveEntry entry);
}
