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
    /// Access a registered system by type. Used by extension methods like .Http(), .PostgreSql().
    /// </summary>
    public TSystem Get<TSystem>() where TSystem : IPluggedSystem
    {
        return Stove.GetSystem<TSystem>();
    }
}
