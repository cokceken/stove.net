using Stove.Net.Core;

namespace Stove.Net.Http;

/// <summary>
/// Extension methods to register and access the HTTP system.
/// </summary>
public static class StoveHttpExtensions
{
    /// <summary>
    /// Register an HTTP client system with the Stove builder.
    /// </summary>
    public static StoveBuilder WithHttpClient(
        this StoveBuilder builder)
    {
        var system = new HttpClientSystem();
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the HTTP system in a validation block.
    /// </summary>
    public static async Task Http(
        this ValidationDsl dsl,
        Func<HttpClientSystem, Task> validation)
    {
        var system = dsl.Get<HttpClientSystem>();
        await validation(system);
    }
}