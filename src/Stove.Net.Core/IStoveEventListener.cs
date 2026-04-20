namespace Stove.Net.Core;

/// <summary>
/// Listener for structured Stove test lifecycle and operation events.
/// Register via StoveBuilder to receive events from all systems.
/// </summary>
public interface IStoveEventListener
{
    void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems);
    void OnTestStarted(string testId, string testName, string specName);
    void OnEntryRecorded(StoveEntry entry);
    void OnSpanRecorded(StoveSpan span);
    void OnTestEnded(string testId, TimeSpan duration, string? error);
    void OnRunEnded(int total, int passed, int failed, TimeSpan duration);
}
