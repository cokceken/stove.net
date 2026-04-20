using Stove.Net.Core;

namespace Stove.Net.Tests.Dashboard.Tests;

/// <summary>
/// Captures all IStoveEventListener callbacks for assertion in tests.
/// </summary>
internal sealed class CaptureListener : IStoveEventListener
{
    public List<(string runId, string appName, IReadOnlyList<string> systems)> RunsStarted { get; } = new();
    public List<(string testId, string testName, string specName)> TestsStarted { get; } = new();
    public List<StoveEntry> EntriesRecorded { get; } = new();
    public List<(string testId, TimeSpan duration, string? error)> TestsEnded { get; } = new();
    public List<(int total, int passed, int failed, TimeSpan duration)> RunsEnded { get; } = new();

    public void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems)
        => RunsStarted.Add((runId, appName, systems));

    public void OnTestStarted(string testId, string testName, string specName)
        => TestsStarted.Add((testId, testName, specName));

    public void OnEntryRecorded(StoveEntry entry)
        => EntriesRecorded.Add(entry);

    public void OnTestEnded(string testId, TimeSpan duration, string? error)
        => TestsEnded.Add((testId, duration, error));

    public void OnRunEnded(int total, int passed, int failed, TimeSpan duration)
        => RunsEnded.Add((total, passed, failed, duration));
}
