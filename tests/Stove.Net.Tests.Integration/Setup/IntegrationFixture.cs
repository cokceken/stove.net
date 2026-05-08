using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stove.Net.Core;
using Stove.Net.Core.Logging;
using Stove.Net.Http;
using Stove.Net.PostgreSql;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Tracing;
using Stove.Net.WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Stove.Net.Tests.Integration.Setup;

/// <summary>
/// Full integration fixture: HTTP + PostgreSQL + WireMock.
/// Boots the ExampleApp backed by real containers and an in-process mock server.
/// Framework-agnostic — uses standard xUnit IAsyncLifetime, not Stove-specific base classes.
/// </summary>
public class IntegrationFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private StoveInstance? _stove;

    /// <summary>Captures application logs for inclusion in failure reports.</summary>
    public StoveLogCapture LogCapture { get; } = new();

    public StoveInstance Stove => _stove
                                  ?? throw new InvalidOperationException(
                                      "Stove is not initialized. Await InitializeAsync() first.");

    public IServiceProvider Services => _factory?.Services
                                        ?? throw new InvalidOperationException(
                                            "WebApplicationFactory is not initialized.");

    public async ValueTask InitializeAsync()
    {
        var builder = StoveBuilder.Create()
            .WithOtlpTracing()
            .WithHttpClient()
            .WithPostgreSql(opts =>
            {
                opts.ConfigureExposedConfiguration = connectionString =>
                [
                    new KeyValuePair<string, string>(
                        "ConnectionStrings:DefaultConnection", connectionString)
                ];
            })
            .WithWireMock(opts =>
            {
                opts.ConfigureExposedConfiguration = url =>
                [
                    new KeyValuePair<string, string>(
                        "ExternalApis:NotificationUrl", url)
                ];
            })
            .WithLogCapture(LogCapture);

        _stove = await builder.RunAsync();

        var stoveConfig = _stove.CollectConfiguration().ToList();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((_, config) =>
                {
                    if (stoveConfig.Count > 0)
                        config.AddInMemoryCollection(stoveConfig!);
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.AddProvider(LogCapture.CreateProvider());
                    });
                });
            });

        _stove.GetSystem<HttpClientSystem>().SetHttpClient(_factory.CreateClient());

        using var scope = _factory.Services.CreateScope();
        await _stove.NotifyAfterRunAsync(scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose factory first so the app flushes OTLP spans while the receiver is still running
        if (_factory != null) await _factory.DisposeAsync();

        // Brief wait for in-flight gRPC exports to complete after flush
        await Task.Delay(200);

        if (_stove != null) await _stove.DisposeAsync();
    }
}