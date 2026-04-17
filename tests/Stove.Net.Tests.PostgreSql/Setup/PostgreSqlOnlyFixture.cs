using Stove.Net.Core;
using Stove.Net.PostgreSql;
using Xunit;

namespace Stove.Net.Tests.PostgreSql.Setup;

/// <summary>
/// Fixture that boots only the PostgreSQL system (Testcontainer).
/// No web application is involved — tests raw container operations.
/// </summary>
public class PostgreSqlOnlyFixture : IAsyncLifetime
{
    public StoveInstance Stove { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Stove = await StoveBuilder.Create()
            .WithPostgreSql(opts =>
            {
                opts.MigrationSql.Add("""
                    CREATE TABLE products (
                        id SERIAL PRIMARY KEY,
                        name TEXT NOT NULL,
                        price DECIMAL(10,2) NOT NULL
                    )
                    """);
            })
            .RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Stove.DisposeAsync();
    }
}
