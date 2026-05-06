using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Tracing;
using Xunit;

namespace Stove.Net.Tests.Http.Setup;

/// <summary>
/// Fixture that boots the ExampleApp with only the HTTP system.
/// PostgreSQL is replaced with an in-memory database so no container is needed.
/// Framework-agnostic — uses standard xUnit IAsyncLifetime, not Stove-specific base classes.
/// </summary>
public class HttpOnlyFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private StoveInstance? _stove;

    public StoveInstance Stove => _stove
                                  ?? throw new InvalidOperationException(
                                      "Stove is not initialized. Await InitializeAsync() first.");

    public async ValueTask InitializeAsync()
    {
        var builder = StoveBuilder.Create()
            .WithOtlpTracing()
            .WithHttpClient();

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
                    // Remove all EF Core / Npgsql registrations for AppDbContext
                    var descriptorsToRemove = services
                        .Where(d =>
                            d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                            d.ServiceType == typeof(DbContextOptions) ||
                            d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                        .ToList();

                    foreach (var d in descriptorsToRemove)
                        services.Remove(d);

                    // Replace with in-memory database
                    services.AddDbContext<AppDbContext>(opts =>
                        opts.UseInMemoryDatabase("StoveHttpTests"));

                    // Provide a dummy Kafka ProducerConfig (no broker in HTTP-only tests)
                    var existingKafka = services.FirstOrDefault(d =>
                        d.ServiceType == typeof(Confluent.Kafka.ProducerConfig));
                    if (existingKafka != null) services.Remove(existingKafka);
                    services.AddSingleton(new Confluent.Kafka.ProducerConfig
                    {
                        BootstrapServers = "localhost:9092"
                    });

                    // Remove Redis registration (no Redis container in HTTP-only tests)
                    var existingRedis = services.FirstOrDefault(d =>
                        d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));
                    if (existingRedis != null) services.Remove(existingRedis);
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