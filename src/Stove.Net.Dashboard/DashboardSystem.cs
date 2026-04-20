using Google.Protobuf.WellKnownTypes;
using Stove.Dashboard.V1;
using Stove.Net.Core;
using Stove.Net.Dashboard.Internal;

namespace Stove.Net.Dashboard;

/// <summary>
/// Stove system that streams lifecycle and assertion events to the Stove dashboard UI.
/// Register via .WithDashboard() on StoveBuilder.
///
/// The dashboard must be running at the configured address (default: http://localhost:4041).
/// If the dashboard is unreachable the emitter self-disables after MaxConsecutiveFailures
/// to avoid disrupting the test run.
/// </summary>
public sealed class DashboardSystem(DashboardSystemOptions options) : IPluggedSystem, IStoveEventListener
{
    private readonly DashboardEmitter _emitter = new(options);
    private string _runId = string.Empty;

    public Task RunAsync() => Task.CompletedTask;

    public Task CleanupAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _emitter.DisposeAsync();
    }

    // ---- IStoveEventListener ----

    public void OnRunStarted(string runId, string appName, IReadOnlyList<string> systems)
    {
        _runId = runId;
        var appNameResolved = string.IsNullOrEmpty(appName) ? options.AppName : appName;

        Enqueue(new DashboardEvent
        {
            RunId = runId,
            RunStarted = new RunStartedEvent
            {
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                AppName = appNameResolved,
                StoveVersion = GetStoveVersion(),
                Systems = { systems }
            }
        });
    }

    public void OnTestStarted(string testId, string testName, string specName)
    {
        Enqueue(new DashboardEvent
        {
            RunId = _runId,
            TestStarted = new TestStartedEvent
            {
                TestId = testId,
                TestName = testName,
                SpecName = specName ?? string.Empty,
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            }
        });
    }

    public void OnEntryRecorded(StoveEntry entry)
    {
        Enqueue(new DashboardEvent
        {
            RunId = _runId,
            EntryRecorded = new EntryRecordedEvent
            {
                TestId = entry.TestId ?? string.Empty,
                Timestamp = Timestamp.FromDateTimeOffset(entry.Timestamp),
                System = entry.System ?? string.Empty,
                Action = entry.Action ?? string.Empty,
                Result = entry.Result.ToString(),
                Input = entry.Input ?? string.Empty,
                Output = entry.Output ?? string.Empty,
                Expected = entry.Expected ?? string.Empty,
                Actual = entry.Actual ?? string.Empty,
                Error = entry.Error ?? string.Empty
            }
        });
    }

    public void OnTestEnded(string testId, TimeSpan duration, string? error)
    {
        Enqueue(new DashboardEvent
        {
            RunId = _runId,
            TestEnded = new TestEndedEvent
            {
                TestId = testId,
                Status = error == null ? "passed" : "failed",
                DurationMs = (long)duration.TotalMilliseconds,
                Error = error ?? string.Empty,
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            }
        });
    }

    public void OnSpanRecorded(StoveSpan span)
    {
        var evt = new SpanRecordedEvent
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            OperationName = span.OperationName,
            ServiceName = span.ServiceName,
            StartTimeNanos = span.Start.ToUnixTimeMilliseconds() * 1_000_000,
            EndTimeNanos = span.End.ToUnixTimeMilliseconds() * 1_000_000,
            Status = span.Status
        };
        foreach (var (k, v) in span.Attributes)
            evt.Attributes.Add(k, v);
        if (span.Exception is { } ex)
            evt.Exception = new ExceptionInfo
            {
                Type = ex.Type,
                Message = ex.Message,
                StackTrace = { ex.StackTrace }
            };
        Enqueue(new DashboardEvent { RunId = _runId, SpanRecorded = evt });
    }

    public void OnSnapshotRecorded(StoveSnapshot snapshot)
    {
        Enqueue(new DashboardEvent
        {
            RunId = _runId,
            Snapshot = new SnapshotEvent
            {
                TestId = snapshot.TestId,
                System = snapshot.System,
                StateJson = snapshot.StateJson,
                Summary = snapshot.Summary
            }
        });
    }

    public void OnRunEnded(int total, int passed, int failed, TimeSpan duration)
    {
        Enqueue(new DashboardEvent
        {
            RunId = _runId,
            RunEnded = new RunEndedEvent
            {
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                TotalTests = total,
                Passed = passed,
                Failed = failed,
                DurationMs = (long)duration.TotalMilliseconds
            }
        });
    }

    private void Enqueue(DashboardEvent evt) => _emitter.Enqueue(evt);

    private static string GetStoveVersion()
    {
        //this is the current version of the stove dashboard so lets keep it
        return "0.23.3";
        var asm = typeof(DashboardSystem).Assembly;
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}