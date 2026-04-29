using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using Xunit;

namespace Stove.Net.Xunit;

/// <summary>
/// xUnit fixture that integrates Stove with WebApplicationFactory.
/// Boots the app-under-test in-process, starts Testcontainers, and wires
/// container configuration into the app.
///
/// By default, the fixture auto-injects:
/// - Application log capture (via <see cref="StoveLoggerProvider"/>)
/// - HTTP body capture middleware (via <see cref="StoveBodyCaptureMiddleware"/>)
/// - Server-side trace capture (via <see cref="InProcessTraceCollector"/>)
///
/// Override <see cref="ConfigureLogCapture"/>, <see cref="ConfigureBodyCapture"/>,
/// or <see cref="ConfigureTraceCapture"/> to customise or disable each feature.
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
public abstract class StoveFixture<TProgram> : IAsyncLifetime, IStoveFixture
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
    /// Creates an HttpClient backed by the in-process test server.
    /// Call this in InitializeAsync() after base.InitializeAsync() to wire
    /// into HttpClientSystem or any other system that needs it.
    /// </summary>
    public HttpClient CreateClient() => _factory?.CreateClient()
                                         ?? throw new InvalidOperationException(
                                             "WebApplicationFactory is not initialized. Call base.InitializeAsync() first.");

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

    /// <summary>
    /// Override to configure which application logs Stove captures.
    /// Return null to disable application log capture entirely.
    /// Default: captures Information+ logs, excludes Stove.Net and gRPC internals.
    /// </summary>
    protected virtual StoveLogCaptureOptions? ConfigureLogCapture() => new();

    /// <summary>
    /// Override to configure HTTP body capture.
    /// Return null to disable body capture entirely.
    /// Default: captures request and response bodies (truncated at 8192 chars).
    /// </summary>
    protected virtual StoveBodyCaptureOptions? ConfigureBodyCapture() => new();

    /// <summary>
    /// Override to disable or customise server-side trace capture.
    /// Return false to disable trace capture entirely.
    /// Default: true — captures Activity spans from ASP.NET Core, HttpClient, EF Core, etc.
    /// </summary>
    protected virtual bool ConfigureTraceCapture() => true;

    public virtual async ValueTask InitializeAsync()
    {
        var builder = StoveBuilder.Create();
        builder = Configure(builder);

        // Auto-wire trace capture unless disabled
        if (ConfigureTraceCapture())
            builder.WithTraceCapture();

        // Start all systems (containers, etc.) first
        _stove = await builder.RunAsync();

        // Collect configuration from all systems
        var stoveConfig = _stove.CollectConfiguration().ToList();

        // Resolve log capture options
        var logCaptureOptions = ConfigureLogCapture();

        // Resolve body capture options
        var bodyCaptureOptions = ConfigureBodyCapture();

        // Create the WebApplicationFactory with injected configuration
        _factory = new WebApplicationFactory<TProgram>()
            .WithWebHostBuilder(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((_, config) =>
                {
                    if (stoveConfig.Count > 0)
                        config.AddInMemoryCollection(stoveConfig!);
                });

                // Auto-inject Stove logger to capture SUT's ILogger output
                if (logCaptureOptions != null)
                {
                    webBuilder.ConfigureLogging(logging =>
                    {
                        logging.AddProvider(new StoveLoggerProvider(_stove, logCaptureOptions));
                    });
                }

                // Auto-inject body capture middleware via IStartupFilter
                if (bodyCaptureOptions != null)
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IStartupFilter>(
                            new StoveBodyCaptureStartupFilter(_stove, bodyCaptureOptions));
                    });
                }

                ConfigureWebHost(webBuilder);
            });

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

/// <summary>
/// IStartupFilter that injects <see cref="StoveBodyCaptureMiddleware"/> into the
/// ASP.NET Core pipeline automatically. Runs early so it can wrap the response stream
/// before other middleware processes the request.
/// </summary>
internal sealed class StoveBodyCaptureStartupFilter(
    IStoveEventEmitter emitter,
    StoveBodyCaptureOptions options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseStoveBodyCapture(emitter, o =>
            {
                o.CaptureRequest = options.CaptureRequest;
                o.CaptureResponse = options.CaptureResponse;
                o.MaxBodySize = options.MaxBodySize;
                o.Redact = options.Redact;
            });
            next(app);
        };
    }
}