namespace Stove.Net.Core;

/// <summary>
/// Fluent builder for configuring a Stove test environment.
/// Extension methods from component packages (e.g., Stove.Net.Http) add .WithXxx() methods.
/// </summary>
public sealed class StoveBuilder
{
    internal StoveInstance Instance { get; } = new();

    private StoveBuilder() { }

    /// <summary>Create a new StoveBuilder to configure and run a test environment.</summary>
    public static StoveBuilder Create() => new();

    /// <summary>Register a plugged system with the default name.</summary>
    public StoveBuilder WithSystem<TSystem>(TSystem system) where TSystem : IPluggedSystem
    {
        Instance.Register(system);
        return this;
    }

    /// <summary>Register a named plugged system.</summary>
    public StoveBuilder WithSystem<TSystem>(TSystem system, string name) where TSystem : IPluggedSystem
    {
        Instance.Register(system, name);
        return this;
    }

    /// <summary>
    /// Add an event listener that receives lifecycle and per-operation events from all systems.
    /// </summary>
    public StoveBuilder WithListener(IStoveEventListener listener)
    {
        Instance.AddListener(listener);
        return this;
    }

    /// <summary>
    /// Enable the built-in console reporter. Prints a structured line per DSL action.
    /// </summary>
    public StoveBuilder WithConsoleReporter()
    {
        Instance.AddListener(new ConsoleEventListener());
        return this;
    }

    /// <summary>Start all registered systems and return the configured Stove instance.</summary>
    public async Task<StoveInstance> RunAsync()
    {
        await Instance.RunSystemsAsync();
        return Instance;
    }
}
