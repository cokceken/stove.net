using System.Diagnostics;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using Xunit;

namespace Stove.Net.Tests.Dashboard.Tests;

/// <summary>
/// Tests the event model — listener callbacks are invoked with correct data.
/// Uses a CaptureListener to avoid needing a real dashboard server.
/// </summary>
public class EventModelTests
{
    [Fact]
    public async Task Run_lifecycle_events_are_emitted_in_order()
    {
        var listener = new CaptureListener();

        var stove = new StoveInstance();
        stove.AddListener(listener);

        await stove.RunSystemsAsync();

        stove.NotifyTestStarted("t1", "My_test", "MySpec");
        stove.Emit(new StoveEntry
        {
            TestId = "t1",
            System = "Fake",
            Action = "ShouldQuery",
            Result = EntryResult.Success,
            Input = "SELECT 1",
            Output = "1 row(s)"
        });
        stove.NotifyTestEnded(passed: true);

        await stove.DisposeAsync();

        Assert.Single(listener.RunsStarted);
        Assert.Single(listener.TestsStarted);
        Assert.Single(listener.EntriesRecorded);
        Assert.Single(listener.TestsEnded);
        Assert.Single(listener.RunsEnded);

        var entry = listener.EntriesRecorded[0];
        Assert.Equal("Fake", entry.System);
        Assert.Equal("ShouldQuery", entry.Action);
        Assert.Equal(EntryResult.Success, entry.Result);
        Assert.Equal("SELECT 1", entry.Input);
        Assert.Equal("1 row(s)", entry.Output);

        var testStarted = listener.TestsStarted[0];
        Assert.Equal("t1", testStarted.testId);
        Assert.Equal("My_test", testStarted.testName);
        Assert.Equal("MySpec", testStarted.specName);

        var testEnded = listener.TestsEnded[0];
        Assert.Equal("t1", testEnded.testId);
        Assert.Null(testEnded.error);
    }

    [Fact]
    public async Task Failed_entry_records_error()
    {
        var listener = new CaptureListener();

        var stove = new StoveInstance();
        stove.AddListener(listener);
        await stove.RunSystemsAsync();

        stove.NotifyTestStarted("t2", "Failing_test", "MySpec");
        stove.Emit(new StoveEntry
        {
            TestId = "t2",
            System = "PostgreSql",
            Action = "ShouldQuery",
            Result = EntryResult.Failed,
            Input = "SELECT * FROM missing_table",
            Error = "relation \"missing_table\" does not exist"
        });
        stove.NotifyTestEnded(passed: false, error: "Assertion failed");

        await stove.DisposeAsync();

        var entry = listener.EntriesRecorded[0];
        Assert.Equal(EntryResult.Failed, entry.Result);
        Assert.Equal("relation \"missing_table\" does not exist", entry.Error);
        Assert.True(entry.IsFailed);
        Assert.False(entry.IsSuccess);

        var testEnded = listener.TestsEnded[0];
        Assert.Equal("Assertion failed", testEnded.error);

        var runEnded = listener.RunsEnded[0];
        Assert.Equal(1, runEnded.total);
        Assert.Equal(0, runEnded.passed);
        Assert.Equal(1, runEnded.failed);
    }

    [Fact]
    public async Task Console_reporter_does_not_throw()
    {
        // Smoke test: console reporter should not throw for any valid entry
        var stove = new StoveInstance();
        stove.AddListener(new ConsoleEventListener());

        await stove.RunSystemsAsync();
        stove.NotifyTestStarted("t3", "Console_test", "Spec");
        stove.Emit(new StoveEntry
        {
            TestId = "t3",
            System = "Redis",
            Action = "ShouldExist",
            Result = EntryResult.Success,
            Input = "my:key",
            Output = "exists"
        });
        stove.NotifyTestEnded(true);
        await stove.DisposeAsync();
        // No exception = pass
    }

    [Fact]
    public async Task Dashboard_system_does_not_throw_when_unreachable()
    {
        // DashboardSystem should silently self-disable when the server is unreachable.
        // It will queue events and fail to send them, but never throw at the test level.
        var listener = new CaptureListener();

        var stove = await StoveBuilder.Create()
            .WithListener(listener)
            .WithDashboard(opts =>
            {
                opts.Host = "localhost";
                opts.Port = 19999; // nothing listening here
                opts.MaxConsecutiveFailures = 1; // disable quickly
                opts.DrainTimeout = TimeSpan.FromMilliseconds(500);
            })
            .RunAsync();

        stove.NotifyTestStarted("t4", "Dashboard_unreachable", "Spec");
        stove.Emit(new StoveEntry
        {
            TestId = "t4", System = "Test", Action = "ShouldExist",
            Result = EntryResult.Success, Input = "key"
        });
        stove.NotifyTestEnded(true);

        // DisposeAsync drains the dashboard emitter — must not throw
        await stove.DisposeAsync();

        // Our own listener still received the events
        Assert.Single(listener.TestsStarted);
        Assert.Single(listener.EntriesRecorded);
    }

    [Fact]
    public async Task Validate_emits_root_span_with_caller_name()
    {
        var listener = new CaptureListener();

        var stove = new StoveInstance();
        stove.AddListener(listener);
        await stove.RunSystemsAsync();

        stove.NotifyTestStarted("t5", "Span_test", "SpanSpec");

        await stove.Validate(async _ => await Task.CompletedTask);

        stove.NotifyTestEnded(true);
        await stove.DisposeAsync();

        // Validate() emits a root span
        Assert.Single(listener.SpansRecorded);
        var rootSpan = listener.SpansRecorded[0];
        Assert.Equal("Validate_emits_root_span_with_caller_name", rootSpan.OperationName);
        Assert.Equal("Validate", rootSpan.ServiceName);
        Assert.Equal("OK", rootSpan.Status);
        Assert.Empty(rootSpan.ParentSpanId);
        Assert.NotEmpty(rootSpan.TraceId);
        Assert.NotEmpty(rootSpan.SpanId);
        Assert.True(rootSpan.DurationMs >= 0);
    }

    [Fact]
    public async Task Failed_validate_emits_error_span()
    {
        var listener = new CaptureListener();

        var stove = new StoveInstance();
        stove.AddListener(listener);
        await stove.RunSystemsAsync();

        stove.NotifyTestStarted("t6", "FailSpan_test", "SpanSpec");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stove.Validate(async _ =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("boom");
            }));

        stove.NotifyTestEnded(false, "boom");
        await stove.DisposeAsync();

        Assert.Single(listener.SpansRecorded);
        var span = listener.SpansRecorded[0];
        Assert.Equal("ERROR", span.Status);
        Assert.NotNull(span.Exception);
        Assert.Equal("InvalidOperationException", span.Exception.Type);
        Assert.Equal("boom", span.Exception.Message);
    }

    [Fact]
    public async Task InProcessTraceCollector_captures_activity_spans()
    {
        var listener = new CaptureListener();

        var stove = await StoveBuilder.Create()
            .WithListener(listener)
            .WithTraceCapture(name => name == "TestApp.Source")
            .RunAsync();

        stove.NotifyTestStarted("t7", "Activity_capture", "TraceSpec");

        // Simulate a server-side ActivitySource (like ASP.NET Core would emit)
        using var source = new ActivitySource("TestApp.Source");
        await stove.Validate(async _ =>
        {
            using var activity = source.StartActivity("GET /api/products", ActivityKind.Server);
            activity?.SetTag("http.method", "GET");
            activity?.SetTag("http.route", "/api/products");
            activity?.SetTag("http.status_code", "200");
            await Task.CompletedTask;
        });

        stove.NotifyTestEnded(true);
        await stove.DisposeAsync();

        // Should have: root Validate span + captured server activity
        var serverSpans = listener.SpansRecorded
            .Where(s => s.ServiceName == "TestApp.Source").ToList();
        Assert.Single(serverSpans);

        var serverSpan = serverSpans[0];
        Assert.Equal("GET /api/products", serverSpan.OperationName);
        Assert.Equal("GET", serverSpan.Attributes["http.method"]);
        Assert.Equal("/api/products", serverSpan.Attributes["http.route"]);
        Assert.Equal("200", serverSpan.Attributes["http.status_code"]);

        // Server span should share the same trace ID as the Validate root span
        var rootSpan = listener.SpansRecorded.First(s => s.ServiceName == "Validate");
        Assert.Equal(rootSpan.TraceId, serverSpan.TraceId);
    }
}
