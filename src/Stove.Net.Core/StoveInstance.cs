using Stove.Net.Core.Exceptions;

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

    private string _currentTestId = string.Empty;
    private DateTimeOffset _testStartedAt;
    private int _totalTests;
    private int _passedTests;
    private int _failedTests;
    private DateTimeOffset _runStartedAt = DateTimeOffset.UtcNow;

    // ---- IStoveEventEmitter ----

    /// <inheritdoc/>
    public string CurrentTestId => _currentTestId;

    /// <inheritdoc/>
    public void Emit(StoveEntry entry)
    {
        foreach (var listener in _listeners)
            listener.OnEntryRecorded(entry);
    }

    // ---- Listener registration ----

    /// <summary>
    /// Register an event listener. Listeners receive all lifecycle and operation events.
    /// </summary>
    public void AddListener(IStoveEventListener listener) => _listeners.Add(listener);

    // ---- Test lifecycle ----

    /// <summary>
    /// Notify listeners that a new test has started. Call this before each test method.
    /// </summary>
    public void NotifyTestStarted(string testId, string testName, string specName = "")
    {
        _currentTestId = testId;
        _testStartedAt = DateTimeOffset.UtcNow;
        _totalTests++;
        foreach (var listener in _listeners)
            listener.OnTestStarted(testId, testName, specName);
    }

    /// <summary>
    /// Notify listeners that the current test has ended. Call this after each test method.
    /// </summary>
    public void NotifyTestEnded(bool passed, string? error = null)
    {
        var duration = DateTimeOffset.UtcNow - _testStartedAt;
        if (passed) _passedTests++;
        else _failedTests++;
        foreach (var listener in _listeners)
            listener.OnTestEnded(_currentTestId, duration, error);
        _currentTestId = string.Empty;
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

    /// <summary>Entry point for test validation. Use this in your test methods.</summary>
    public async Task Validate(Func<ValidationDsl, Task> validation)
    {
        var dsl = new ValidationDsl(this);
        await validation(dsl);
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