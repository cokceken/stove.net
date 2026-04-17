using Stove.Net.Core;

namespace Stove.Net.WireMock;

/// <summary>
/// Extension methods to register and access the WireMock system.
/// </summary>
public static class StoveWireMockExtensions
{
    /// <summary>
    /// Register a WireMock system (in-process HTTP mock server) in the Stove builder.
    /// </summary>
    public static StoveBuilder WithWireMock(
        this StoveBuilder builder,
        Action<WireMockSystemOptions>? configure = null)
    {
        var options = new WireMockSystemOptions();
        configure?.Invoke(options);

        var system = new WireMockSystem(options);
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the WireMock system in a validation block.
    /// </summary>
    public static async Task WireMock(
        this ValidationDsl dsl,
        Func<WireMockSystem, Task> validation)
    {
        var system = dsl.Get<WireMockSystem>();
        await validation(system);
    }
}
