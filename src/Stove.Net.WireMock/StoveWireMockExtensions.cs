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
        builder.WithSystem(new WireMockSystem(options));
        return builder;
    }

    /// <summary>
    /// Register a named WireMock system. Use when you need to mock multiple external services.
    /// </summary>
    public static StoveBuilder WithWireMock(
        this StoveBuilder builder,
        string name,
        Action<WireMockSystemOptions>? configure = null)
    {
        var options = new WireMockSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new WireMockSystem(options), name);
        return builder;
    }

    /// <summary>
    /// Access the default WireMock system in a validation block.
    /// </summary>
    public static async Task WireMock(this ValidationDsl dsl, Func<WireMockSystem, Task> validation)
    {
        await validation(dsl.Get<WireMockSystem>());
    }

    /// <summary>
    /// Access a named WireMock system in a validation block.
    /// </summary>
    public static async Task WireMock(this ValidationDsl dsl, string name, Func<WireMockSystem, Task> validation)
    {
        await validation(dsl.Get<WireMockSystem>(name));
    }
}
