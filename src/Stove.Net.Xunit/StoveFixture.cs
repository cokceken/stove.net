using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Xunit;

namespace Stove.Net.Xunit;

/// <summary>
/// xUnit fixture that integrates Stove with WebApplicationFactory.
/// Boots the app-under-test in-process, starts Testcontainers, and wires
/// container configuration into the app.
///
/// Usage:
/// <code>
/// public class MyFixture : StoveFixture&lt;Program&gt;
/// {
///     protected override StoveBuilder Configure(StoveBuilder builder)
///         => builder
///             .WithHttpClient()
///             .WithPostgreSql(opts => { ... });
/// }
/// </code>
/// </summary>
public abstract class StoveFixture<TProgram> : IAsyncLifetime
    where TProgram : class
{
    private WebApplicationFactory<TProgram>? _factory;
    private StoveInstance? _stove;

    /// <summary>
    /// The configured Stove instance. Available after InitializeAsync().
    /// </summary>
    public StoveInstance Stove => _stove
                                  ?? throw new InvalidOperationException(
                                      "Stove is not initialized. Await InitializeAsync() first.");

    /// <summary>
    /// The application's service provider. Available after InitializeAsync().
    /// Useful for resolving services to run migrations, seed data, etc.
    /// </summary>
    public IServiceProvider Services => _factory?.Services
                                        ?? throw new InvalidOperationException(
                                            "WebApplicationFactory is not initialized. Await InitializeAsync() first.");

    /// <summary>
    /// Override to configure which systems (HTTP, PostgreSQL, etc.) to use.
    /// </summary>
    protected abstract StoveBuilder Configure(StoveBuilder builder);

    /// <summary>
    /// Override to further customise the web host — e.g. replace services,
    /// add middleware, or swap a real database for an in-memory one.
    /// Called after Stove configuration has been injected.
    /// </summary>
    protected virtual void ConfigureWebHost(IWebHostBuilder builder) { }

    public virtual async ValueTask InitializeAsync()
    {
        var builder = StoveBuilder.Create();
        builder = Configure(builder);

        // Start all systems (containers, etc.) first
        _stove = await builder.RunAsync();

        // Collect configuration from all systems
        var stoveConfig = _stove.CollectConfiguration().ToList();

        // Create the WebApplicationFactory with injected configuration
        _factory = new WebApplicationFactory<TProgram>()
            .WithWebHostBuilder(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((_, config) =>
                {
                    if (stoveConfig.Count > 0)
                        config.AddInMemoryCollection(stoveConfig!);
                });

                ConfigureWebHost(webBuilder);
            });

        // Create the HttpClient and inject it into the HTTP system if registered
        if (_stove.TryGetSystem<HttpClientSystem>(out var httpSystem))
        {
            var httpClient = _factory.CreateClient();
            httpSystem!.SetHttpClient(httpClient);
        }

        // Notify after-run-aware systems
        using var scope = _factory.Services.CreateScope();
        await _stove.NotifyAfterRunAsync(scope.ServiceProvider);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_stove != null)
            await _stove.DisposeAsync();

        if (_factory != null)
            await _factory.DisposeAsync();
    }
}