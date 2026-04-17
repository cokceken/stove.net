namespace Stove.Net.Core;

/// <summary>
/// Fluent builder for configuring a Stove test environment.
/// Extension methods from component packages (e.g., Stove.Net.Http) add .WithXxx() methods.
/// </summary>
public sealed class StoveBuilder
{
    internal StoveInstance Instance { get; } = new();

    private StoveBuilder() { }

    /// <summary>
    /// Create a new StoveBuilder to configure and run a test environment.
    /// </summary>
    public static StoveBuilder Create() => new();

    /// <summary>
    /// Register a plugged system with the Stove instance.
    /// Typically called by .WithXxx() extension methods.
    /// </summary>
    public StoveBuilder WithSystem<TSystem>(TSystem system) where TSystem : IPluggedSystem
    {
        Instance.Register(system);
        return this;
    }

    /// <summary>
    /// Start all registered systems and return the configured Stove instance.
    /// Call this after all .WithXxx() registrations.
    /// </summary>
    public async Task<StoveInstance> RunAsync()
    {
        await Instance.RunSystemsAsync();
        return Instance;
    }
}
