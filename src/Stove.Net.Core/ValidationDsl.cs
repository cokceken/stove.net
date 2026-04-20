namespace Stove.Net.Core;

/// <summary>
/// DSL context passed to stove.Validate(). Provides access to registered systems
/// via typed accessor methods added by extension packages.
/// </summary>
public sealed class ValidationDsl
{
    internal StoveInstance Stove { get; }

    internal ValidationDsl(StoveInstance stove)
    {
        Stove = stove;
    }

    /// <summary>
    /// Access the default-named registered system by type.
    /// Used by extension methods like .Http(), .PostgreSql().
    /// </summary>
    public TSystem Get<TSystem>() where TSystem : IPluggedSystem
        => Stove.GetSystem<TSystem>();

    /// <summary>
    /// Access a named registered system by type.
    /// Used when multiple instances of the same system type are registered.
    /// </summary>
    public TSystem Get<TSystem>(string name) where TSystem : IPluggedSystem
        => Stove.GetSystem<TSystem>(name);
}
