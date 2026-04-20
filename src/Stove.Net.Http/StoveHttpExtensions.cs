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
    public static StoveBuilder WithHttpClient(this StoveBuilder builder)
    {
        builder.WithSystem(new HttpClientSystem());
        return builder;
    }

    /// <summary>
    /// Register a named HTTP client system. Use when you need multiple HTTP clients.
    /// </summary>
    public static StoveBuilder WithHttpClient(this StoveBuilder builder, string name)
    {
        builder.WithSystem(new HttpClientSystem(), name);
        return builder;
    }

    /// <summary>
    /// Access the default HTTP system in a validation block.
    /// </summary>
    public static async Task Http(this ValidationDsl dsl, Func<HttpClientSystem, Task> validation)
    {
        await validation(dsl.Get<HttpClientSystem>());
    }

    /// <summary>
    /// Access a named HTTP system in a validation block.
    /// </summary>
    public static async Task Http(this ValidationDsl dsl, string name, Func<HttpClientSystem, Task> validation)
    {
        await validation(dsl.Get<HttpClientSystem>(name));
    }
}