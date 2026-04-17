using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Stove.Net.Kafka;
using Stove.Net.PostgreSql;
using Stove.Net.Redis;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Xunit;

namespace Stove.Net.Tests.Integration.Setup;

/// <summary>
/// Full integration fixture: HTTP + PostgreSQL + Kafka + Redis (Testcontainers).
/// Boots the ExampleApp backed by real PostgreSQL, Kafka, and Redis containers.
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
            });
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Stove.GetSystem<HttpClientSystem>().SetHttpClient(CreateClient());

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
