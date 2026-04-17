using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Stove.Net.PostgreSql;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Xunit;

namespace Stove.Net.Tests.Integration.Setup;

/// <summary>
/// Full integration fixture: HTTP + PostgreSQL (Testcontainer).
/// Boots the ExampleApp backed by a real PostgreSQL database.
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
            });
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
