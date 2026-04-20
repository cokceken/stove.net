namespace Stove.Net.Core;

/// <summary>
/// Composite key for the system registry: system type + optional name.
/// Systems registered without a name use <see cref="DefaultName"/> ("default").
/// </summary>
public readonly record struct SystemKey(Type SystemType, string Name)
{
    public const string DefaultName = "default";

    /// <summary>Create a key with the default name.</summary>
    public static SystemKey For<TSystem>() where TSystem : IPluggedSystem
        => new(typeof(TSystem), DefaultName);

    /// <summary>Create a named key.</summary>
    public static SystemKey For<TSystem>(string name) where TSystem : IPluggedSystem
        => new(typeof(TSystem), name);
}
