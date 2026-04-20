using Stove.Net.Core.Exceptions;

namespace Stove.Net.Core;

/// <summary>
/// The central orchestrator that holds registered systems and manages their lifecycle.
/// Systems are keyed by (Type, Name) — unnamed registrations use the "default" name,
/// enabling multiple named instances of the same system type (e.g., two PostgreSQL databases).
/// Inspired by Trendyol/stove's Stove class.
/// </summary>
public sealed class StoveInstance : IAsyncDisposable
{
    private readonly Dictionary<SystemKey, IPluggedSystem> _systems = new();

    /// <summary>
    /// Register a plugged system with the default name.
    /// </summary>
    public void Register<TSystem>(TSystem system) where TSystem : IPluggedSystem
        => Register(system, SystemKey.DefaultName);

    /// <summary>
    /// Register a named plugged system. Use this when you need multiple instances
    /// of the same system type (e.g., two PostgreSQL databases).
    /// </summary>
    public void Register<TSystem>(TSystem system, string name) where TSystem : IPluggedSystem
        => _systems[new SystemKey(typeof(TSystem), name)] = system;

    /// <summary>
    /// Get the default-named registered system by type, or throw if not registered.
    /// </summary>
    public TSystem GetSystem<TSystem>() where TSystem : IPluggedSystem
        => GetSystem<TSystem>(SystemKey.DefaultName);

    /// <summary>
    /// Get a named registered system by type, or throw if not registered.
    /// </summary>
    public TSystem GetSystem<TSystem>(string name) where TSystem : IPluggedSystem
    {
        var key = new SystemKey(typeof(TSystem), name);
        if (_systems.TryGetValue(key, out var system))
            return (TSystem)system;

        throw new SystemNotRegisteredException(typeof(TSystem), name);
    }

    /// <summary>
    /// Try to get the default-named registered system by type.
    /// </summary>
    public bool TryGetSystem<TSystem>(out TSystem? system) where TSystem : class, IPluggedSystem
        => TryGetSystem(SystemKey.DefaultName, out system);

    /// <summary>
    /// Try to get a named registered system by type.
    /// </summary>
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

    /// <summary>
    /// Returns all registered systems that implement the given interface,
    /// across all names.
    /// </summary>
    public IEnumerable<T> GetSystems<T>() => _systems.Values.OfType<T>();

    /// <summary>
    /// Returns all configuration key-value pairs from systems that implement IExposesConfiguration.
    /// Used by the application host to inject container connection strings, etc.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> CollectConfiguration()
    {
        foreach (var system in _systems.Values.OfType<IExposesConfiguration>())
        {
            foreach (var kvp in system.Configuration())
                yield return kvp;
        }
    }

    /// <summary>
    /// Start all registered systems (containers, clients, etc.).
    /// </summary>
    public async Task RunSystemsAsync()
    {
        foreach (var system in _systems.Values)
            await system.RunAsync();
    }

    /// <summary>
    /// Notify all IAfterRunAware systems that the application has started.
    /// </summary>
    public async Task NotifyAfterRunAsync(IServiceProvider serviceProvider)
    {
        foreach (var system in _systems.Values.OfType<IAfterRunAware>())
            await system.AfterRunAsync(serviceProvider);
    }

    /// <summary>
    /// Clean up all systems between tests.
    /// </summary>
    public async Task CleanupAsync()
    {
        foreach (var system in _systems.Values)
            await system.CleanupAsync();
    }

    /// <summary>
    /// Entry point for test validation. Use this in your test methods.
    /// </summary>
    public async Task Validate(Func<ValidationDsl, Task> validation)
    {
        var dsl = new ValidationDsl(this);
        await validation(dsl);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var system in _systems.Values)
            await system.DisposeAsync();

        _systems.Clear();
    }
}
