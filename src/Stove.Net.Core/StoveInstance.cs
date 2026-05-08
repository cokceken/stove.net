using System.Diagnostics;
using System.Runtime.CompilerServices;
using Stove.Net.Core.Exceptions;
using Stove.Net.Core.Logging;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Core;

/// <summary>
/// The central orchestrator that holds registered systems and manages their lifecycle.
/// Systems are keyed by (Type, Name) — unnamed registrations use the "default" name,
/// enabling multiple named instances of the same system type (e.g., two PostgreSQL databases).
/// Also acts as the event bus: collects IStoveEventListeners and fans out events to them.
/// Inspired by Trendyol/stove's Stove class.
/// </summary>
public sealed class StoveInstance : IAsyncDisposable, IStoveEventEmitter
{
    private readonly Dictionary<SystemKey, IPluggedSystem> _systems = new();
    private readonly List<IStoveEventListener> _listeners = [];
    private readonly string _runId = Guid.NewGuid().ToString("N");

    private static readonly ActivitySource StoveActivitySource = new("Stove.Net");

    /// <summary>Header/baggage key used to propagate test IDs across service boundaries.</summary>
    public const string StoveTestIdHeaderName = "X-Stove-Test-Id";
    public const string StoveTestIdBaggageKey = "stove.test.id";

    // Ensure the Stove ActivitySource always creates activities (for trace propagation)
    // even when no trace collectors are configured.
    private static readonly ActivityListener StoveInternalListener = CreateStoveListener();

    private static ActivityListener CreateStoveListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Stove.Net",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // AsyncLocal ensures correct context in parallel/async test execution
    // (equivalent to Kotlin's InheritableThreadLocal + CoroutineContext)
    private static readonly AsyncLocal<string> AsyncTestId = new();
    private static readonly AsyncLocal<string> AsyncTraceId = new();
    private static readonly AsyncLocal<string> AsyncSpanId = new();
    private static readonly AsyncLocal<DateTimeOffset> AsyncTestStartedAt = new();

    private int _totalTests;
    private int _passedTests;
    private int _failedTests;
    private DateTimeOffset _runStartedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional log capture for application logs. Set via StoveBuilder.WithLogCapture().
    /// </summary>
    internal StoveLogCapture? LogCapture { get; set; }

    /// <summary>
    /// The auto-registered reporter. Null if not yet initialized.
    /// </summary>
    internal StoveReporter? Reporter { get; private set; }

    // ---- IStoveEventEmitter ----

    /// <inheritdoc/>
    public string CurrentTestId => AsyncTestId.Value ?? string.Empty;

    /// <inheritdoc/>
    public string CurrentTraceId => AsyncTraceId.Value ?? string.Empty;

    /// <inheritdoc/>
    public string CurrentSpanId => AsyncSpanId.Value ?? string.Empty;

    /// <inheritdoc/>
    public void Emit(StoveEntry entry)
    {
        // Enrich all entries with test ID in metadata for cross-service correlation
        var enriched = entry;
        var testId = CurrentTestId;
        if (!string.IsNullOrEmpty(testId) && entry.Metadata != null)
        {
            entry.Metadata.TryAdd(StoveTestIdBaggageKey, testId);
        }
        else if (!string.IsNullOrEmpty(testId))
        {
            enriched = entry with
            {
                Metadata = new Dictionary<string, string> { [StoveTestIdBaggageKey] = testId }
            };
        }

        foreach (var listener in _listeners)
            listener.OnEntryRecorded(enriched);
    }

    /// <inheritdoc/>
    public void EmitSpan(StoveSpan span)
    {
        foreach (var listener in _listeners)
            listener.OnSpanRecorded(span);
    }

    /// <inheritdoc/>
    public void EmitSnapshot(StoveSnapshot snapshot)
    {
        foreach (var listener in _listeners)
            listener.OnSnapshotRecorded(snapshot);
    }

    // ---- Listener registration ----

    /// <summary>
    /// Register an event listener. Listeners receive all lifecycle and operation events.
    /// </summary>
    public void AddListener(IStoveEventListener listener) => _listeners.Add(listener);

    /// <summary>
    /// Initialize the auto-registered StoveReporter.
    /// Called by StoveBuilder.RunAsync() before systems start.
    /// </summary>
    internal void InitializeReporter()
    {
        var reporter = new StoveReporter();
        Reporter = reporter;
        _listeners.Add(reporter);
    }

    // ---- Test lifecycle ----

    /// <summary>
    /// Notify listeners that a new test has started. Call this before each test method.
    /// Also adds the test ID to Activity.Baggage for cross-service correlation.
    /// </summary>
    public void NotifyTestStarted(string testId, string testName, string specName = "",
        string[]? testPath = null)
    {
        AsyncTestId.Value = testId;
        AsyncTestStartedAt.Value = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _totalTests);
        Activity.Current?.AddBaggage(StoveTestIdHeaderName, testId);

        // Correlate app log capture with this test
        LogCapture?.SetTestId(testId);

        foreach (var listener in _listeners)
            listener.OnTestStarted(testId, testName, specName, testPath);
    }

    /// <summary>
    /// Notify listeners that the current test has ended. Call this after each test method.
    /// Before emitting OnTestEnded, collects state snapshots from all systems implementing IReportsState.
    /// </summary>
    public void NotifyTestEnded(bool passed, string? error = null)
    {
        var testId = AsyncTestId.Value ?? string.Empty;
        var duration = DateTimeOffset.UtcNow - AsyncTestStartedAt.Value;
        if (passed) Interlocked.Increment(ref _passedTests);
        else Interlocked.Increment(ref _failedTests);

        // Auto-collect snapshots from all reporting systems (like Kotlin's Reports interface)
        foreach (var system in _systems.Values.OfType<IReportsState>())
        {
            try
            {
                var snapshot = system.Report();
                if (snapshot != null)
                    EmitSnapshot(snapshot with { TestId = testId, TraceId = CurrentTraceId });
            }
            catch (Exception)
            {
                // Don't let snapshot collection failures break the test lifecycle
            }
        }

        // Collect container logs from all ICollectsLogs systems
        _ = CollectContainerLogsAsync(testId);

        foreach (var listener in _listeners)
            listener.OnTestEnded(testId, duration, error);

        // Clear app log capture correlation
        LogCapture?.ClearTestId();
        AsyncTestId.Value = string.Empty;
    }

    private async Task CollectContainerLogsAsync(string testId)
    {
        var since = AsyncTestStartedAt.Value;
        foreach (var system in _systems.Values.OfType<ICollectsLogs>())
        {
            try
            {
                var logs = await system.GetLogsSinceAsync(since);
                foreach (var log in logs)
                {
                    Emit(new StoveEntry
                    {
                        TestId = testId,
                        TraceId = CurrentTraceId,
                        System = $"Container:{log.Source}",
                        Action = "Log",
                        Result = log.Stream == "stderr" ? EntryResult.Failed : EntryResult.Success,
                        Output = log.Message,
                        Metadata = new Dictionary<string, string>
                        {
                            ["log.stream"] = log.Stream,
                            ["log.container_id"] = log.ContainerId,
                            ["log.source"] = log.Source
                        }
                    });
                }
            }
            catch (Exception)
            {
                // Don't let log collection failures break the test lifecycle
            }
        }
    }

    // ---- System registration ----

    /// <summary>Register a plugged system with the default name.</summary>
    public void Register<TSystem>(TSystem system) where TSystem : IPluggedSystem
        => Register(system, SystemKey.DefaultName);

    /// <summary>Register a named plugged system.</summary>
    public void Register<TSystem>(TSystem system, string name) where TSystem : IPluggedSystem
    {
        _systems[new SystemKey(typeof(TSystem), name)] = system;
        if (system is IStoveReportingSystem reporting)
            reporting.SetEmitter(this);
    }

    // ---- System access ----

    /// <summary>Get the default-named registered system by type, or throw.</summary>
    public TSystem GetSystem<TSystem>() where TSystem : IPluggedSystem
        => GetSystem<TSystem>(SystemKey.DefaultName);

    /// <summary>Get a named registered system by type, or throw.</summary>
    public TSystem GetSystem<TSystem>(string name) where TSystem : IPluggedSystem
    {
        var key = new SystemKey(typeof(TSystem), name);
        if (_systems.TryGetValue(key, out var system))
            return (TSystem)system;

        throw new SystemNotRegisteredException(typeof(TSystem), name);
    }

    /// <summary>Try to get the default-named registered system by type.</summary>
    public bool TryGetSystem<TSystem>(out TSystem? system) where TSystem : class, IPluggedSystem
        => TryGetSystem(SystemKey.DefaultName, out system);

    /// <summary>Try to get a named registered system by type.</summary>
    public bool TryGetSystem<TSystem>(string name, out TSystem? system) where TSystem : class, IPluggedSystem
    {
        if (_systems.TryGetValue(new SystemKey(typeof(TSystem), name), out var s))
        {
            system = (TSystem)s;
            return true;
        }

        system = null;
        return false;
    }

    /// <summary>Returns all registered systems that implement the given interface, across all names.</summary>
    public IEnumerable<T> GetSystems<T>() => _systems.Values.OfType<T>();

    // ---- Lifecycle ----

    /// <summary>
    /// Returns all configuration key-value pairs from systems that implement IExposesConfiguration.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> CollectConfiguration() => _systems.Values
        .OfType<IExposesConfiguration>().SelectMany(system => system.Configuration());

    /// <summary>Start all registered systems (containers, clients, etc.).</summary>
    public async Task RunSystemsAsync()
    {
        _runStartedAt = DateTimeOffset.UtcNow;

        foreach (var system in _systems.Values)
            await system.RunAsync();

        // Wire reporter with log sources now that all systems are registered
        if (Reporter is not null)
        {
            Reporter.LogSources = _systems.Values.OfType<ICollectsLogs>().ToList();
            if (LogCapture is not null)
                Reporter.AppLogProvider = testId => LogCapture.GetLogs(testId);
        }

        // Fire OnRunStarted AFTER all systems have started — this ensures
        // listener systems (e.g., DashboardSystem) have their infrastructure
        // ready, and the reported system list reflects what actually started.
        var systemNames = _systems.Keys.Select(k => k.SystemType.Name).ToList();
        foreach (var listener in _listeners)
            listener.OnRunStarted(_runId, string.Empty, systemNames);
    }

    /// <summary>Notify all IAfterRunAware systems that the application has started.</summary>
    public async Task NotifyAfterRunAsync(IServiceProvider serviceProvider)
    {
        foreach (var system in _systems.Values.OfType<IAfterRunAware>())
            await system.AfterRunAsync(serviceProvider);
    }

    /// <summary>Clean up all systems between tests.</summary>
    public async Task CleanupAsync()
    {
        foreach (var system in _systems.Values)
            await system.CleanupAsync();
    }

    /// <summary>
    /// Entry point for test validation. Creates a trace (root span) that wraps all
    /// system assertions inside the callback. Also creates a real System.Diagnostics.Activity
    /// so that Activity.Current propagates the trace context (traceparent) to both
    /// in-process and Docker-hosted SUTs — enabling automatic correlation of server spans.
    /// </summary>
    public async Task Validate(
        Func<ValidationDsl, Task> validation,
        [CallerMemberName] string callerName = "")
    {
        var testId = Guid.NewGuid().ToString("N")[..16];
        var traceId = Guid.NewGuid().ToString("N");
        var prevTraceId = AsyncTraceId.Value;
        var prevSpanId = AsyncSpanId.Value;
        AsyncTraceId.Value = traceId;

        // Notify tracing systems about the new trace for span correlation
        foreach (var aware in GetSystems<ITraceContextAware>())
            aware.OnTraceStarted(traceId, testId);

        // Fire test lifecycle so listeners (Dashboard, etc.) know a test is running
        NotifyTestStarted(testId, callerName);

        // Create a real Activity so Activity.Current carries the trace context.
        // Use isRemote: true with a default SpanId so the Activity becomes a root
        // with our custom TraceId. The Activity's auto-generated SpanId becomes
        // rootSpanId, ensuring server-side spans (which are children of this Activity)
        // connect to the same root as our manually emitted spans.
        var activityTraceId = ActivityTraceId.CreateFromString(traceId.AsSpan());
        var parentContext = new ActivityContext(activityTraceId, default,
            ActivityTraceFlags.Recorded, isRemote: true);

        using var activity = StoveActivitySource.StartActivity(
            callerName, ActivityKind.Internal, parentContext);
        activity?.AddBaggage(StoveTestIdBaggageKey, CurrentTestId);

        var rootSpanId = activity?.SpanId.ToString() ?? StoveSpan.NewSpanId();
        AsyncSpanId.Value = rootSpanId;

        var start = DateTimeOffset.UtcNow;
        var dsl = new ValidationDsl(this);
        try
        {
            await validation(dsl);
            EmitSpan(new StoveSpan
            {
                TraceId = traceId, SpanId = rootSpanId, ParentSpanId = string.Empty,
                OperationName = callerName, ServiceName = "Validate",
                Start = start, End = DateTimeOffset.UtcNow, Status = "OK"
            });
            NotifyTestEnded(passed: true);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            EmitSpan(new StoveSpan
            {
                TraceId = traceId, SpanId = rootSpanId, ParentSpanId = string.Empty,
                OperationName = callerName, ServiceName = "Validate",
                Start = start, End = DateTimeOffset.UtcNow, Status = "ERROR",
                Exception = new StoveExceptionInfo(
                    ex.GetType().Name, ex.Message, ex.StackTrace?.Split('\n') ?? [])
            });
            NotifyTestEnded(passed: false, error: ex.Message);

            // Enrich the exception with the Stove execution report so it's
            // always visible in test output regardless of how the runner handles stderr
            var report = Reporter?.ConsumeLastReport();
            if (!string.IsNullOrEmpty(report))
                throw new StoveTestFailureException(ex, report);

            throw;
        }
        finally
        {
            AsyncTraceId.Value = prevTraceId;
            AsyncSpanId.Value = prevSpanId;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var duration = DateTimeOffset.UtcNow - _runStartedAt;
        foreach (var listener in _listeners)
            listener.OnRunEnded(_totalTests, _passedTests, _failedTests, duration);

        foreach (var system in _systems.Values)
            await system.DisposeAsync();

        _systems.Clear();
    }
}
