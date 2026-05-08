using System.Collections.Concurrent;

namespace Stove.Net.Core.Reporting;

/// <summary>
/// Collects test events and generates console reports on failure.
/// Auto-registered as an IStoveEventListener by StoveBuilder.
/// Inspired by Kotlin Stove's StoveReporter.
/// </summary>
public sealed class StoveReporter : IStoveEventListener
{
    private readonly ConcurrentDictionary<string, TestContext> _contexts = new();

    /// <summary>
    /// Log sources (e.g., Testcontainers) set by StoveInstance during initialization.
    /// </summary>
    internal IReadOnlyList<ICollectsLogs> LogSources { get; set; } = [];

    /// <summary>
    /// Optional provider for application-level logs keyed by test ID.
    /// </summary>
    internal Func<string, IReadOnlyList<LogLine>>? AppLogProvider { get; set; }

    /// <summary>
    /// Controls whether reports are rendered to console on failure.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional callback for custom report output (e.g., xUnit's ITestOutputHelper).
    /// If null, writes to Console.Error.
    /// </summary>
    public Action<string>? OutputWriter { get; set; }

    public void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems)
    {
        // No-op for now.
    }

    public void OnTestStarted(string testId, string testName, string specName, string[]? testPath = null)
    {
        var context = new TestContext
        {
            TestId = testId,
            TestName = testName,
            SpecName = specName ?? "",
            StartedAt = DateTimeOffset.UtcNow
        };
        _contexts[testId] = context;
    }

    public void OnEntryRecorded(StoveEntry entry)
    {
        if (_contexts.TryGetValue(entry.TestId, out var context))
        {
            context.Entries.Add(entry);
        }
    }

    public void OnSpanRecorded(StoveSpan span)
    {
        // No-op for now.
    }

    public void OnSnapshotRecorded(StoveSnapshot snapshot)
    {
        if (_contexts.TryGetValue(snapshot.TestId, out var context))
        {
            context.Snapshots.Add(snapshot);
        }
    }

    public void OnTestEnded(string testId, TimeSpan duration, string? error)
    {
        try
        {
            if (error is null || !Enabled)
            {
                return;
            }

            if (!_contexts.TryGetValue(testId, out var context))
            {
                return;
            }

            try
            {
                var containerLogs = CollectContainerLogs(context.StartedAt);
                var appLogs = AppLogProvider?.Invoke(testId) ?? [];

                var report = new TestReport
                {
                    TestId = context.TestId,
                    TestName = context.TestName,
                    SpecName = context.SpecName,
                    Duration = duration,
                    Error = error,
                    Entries = context.Entries.ToList(),
                    Snapshots = context.Snapshots.ToList(),
                    ContainerLogs = containerLogs,
                    ApplicationLogs = appLogs
                };

                var rendered = ConsoleReportRenderer.Render(report);
                var writer = OutputWriter ?? Console.Error.WriteLine;
                writer(rendered);
            }
            catch (Exception ex)
            {
                // Don't let report rendering failures crash the test.
                Console.Error.WriteLine($"[StoveReporter] Failed to render report for test '{testId}': {ex.Message}");
            }
        }
        finally
        {
            _contexts.TryRemove(testId, out _);
        }
    }

    public void OnRunEnded(int total, int passed, int failed, TimeSpan duration)
    {
        // No-op for now.
    }

    private List<ContainerLogEntry> CollectContainerLogs(DateTimeOffset since)
    {
        var logs = new List<ContainerLogEntry>();
        foreach (var source in LogSources)
        {
            try
            {
                var entries = source.GetLogsSinceAsync(since)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                logs.AddRange(entries);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[StoveReporter] Failed to collect logs from source: {ex.Message}");
            }
        }

        logs.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return logs;
    }

    private sealed class TestContext
    {
        public required string TestId { get; init; }
        public required string TestName { get; init; }
        public string SpecName { get; init; } = "";
        public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
        public ConcurrentBag<StoveEntry> Entries { get; } = [];
        public ConcurrentBag<StoveSnapshot> Snapshots { get; } = [];
    }
}
