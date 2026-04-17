using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Stove.Net.Kafka;
using Stove.Net.PostgreSql;
using Stove.Net.Redis;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.WireMock;
using Stove.Net.Xunit;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Stove.Net.Tests.Integration.Setup;

/// <summary>
/// Full integration fixture: HTTP + PostgreSQL + Kafka + Redis + WireMock.
/// Boots the ExampleApp backed by real containers and an in-process mock server.
/// </summary>
public class IntegrationFixture : StoveFixture<Program>
{
    protected override StoveBuilder Configure(StoveBuilder builder)
    {
        return builder
            .WithHttpClient()
            .WithPostgreSql(opts =>
            {
                opts.ConfigureExposedConfiguration = connectionString => new[]
                {
                    new KeyValuePair<string, string>(
                        "ConnectionStrings:DefaultConnection", connectionString)
                };
            })
            .WithKafka(opts =>
            {
                opts.TopicsToConsume.Add("order-events");
                opts.ConfigureExposedConfiguration = bootstrapServers => new[]
                {
                    new KeyValuePair<string, string>(
                        "Kafka:BootstrapServers", bootstrapServers)
                };
            })
            .WithRedis(opts =>
            {
                opts.ConfigureExposedConfiguration = connectionString => new[]
                {
                    new KeyValuePair<string, string>(
                        "Redis:ConnectionString", connectionString)
                };
            })
            .WithWireMock(opts =>
            {
                opts.ConfigureExposedConfiguration = url => new[]
                {
                    new KeyValuePair<string, string>(
                        "ExternalApis:NotificationUrl", url)
                };
            });
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Stove.GetSystem<HttpClientSystem>().SetHttpClient(CreateClient());

        // Set up a default stub for the notification endpoint
        Stove.GetSystem<WireMockSystem>().Stub(
            Request.Create().WithPath("/api/notifications").UsingPost(),
            Response.Create().WithStatusCode(202));

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
