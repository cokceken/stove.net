using Stove.Net.Core.Exceptions;

namespace Stove.Net.Core;

/// <summary>
/// The central orchestrator that holds registered systems and manages their lifecycle.
/// Inspired by Trendyol/stove's Stove class.
/// </summary>
public sealed class StoveInstance : IAsyncDisposable
{
    private readonly Dictionary<Type, IPluggedSystem> _systems = new();

    /// <summary>
    /// Register a plugged system. Called during builder configuration.
    /// </summary>
    public void Register<TSystem>(TSystem system) where TSystem : IPluggedSystem
    {
        _systems[typeof(TSystem)] = system;
    }

    /// <summary>
    /// Get a registered system by type, or throw if not registered.
    /// </summary>
    public TSystem GetSystem<TSystem>() where TSystem : IPluggedSystem
    {
        if (_systems.TryGetValue(typeof(TSystem), out var system))
            return (TSystem)system;

        throw new SystemNotRegisteredException(typeof(TSystem));
    }

    /// <summary>
    /// Try to get a registered system by type.
    /// </summary>
    public bool TryGetSystem<TSystem>(out TSystem? system) where TSystem : class, IPluggedSystem
    {
        if (_systems.TryGetValue(typeof(TSystem), out var s))
        {
            system = (TSystem)s;
            return true;
        }

        system = null;
        return false;
    }

    /// <summary>
    /// Returns all registered systems that implement the given interface.
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
