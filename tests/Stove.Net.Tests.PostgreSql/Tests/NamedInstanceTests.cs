using Stove.Net.Core;
using Stove.Net.PostgreSql;
using Xunit;

namespace Stove.Net.Tests.PostgreSql.Tests;

/// <summary>
/// Demonstrates registering two named PostgreSQL instances in a single Stove setup.
/// This mirrors real production scenarios where a service connects to multiple databases
/// (e.g., a primary database and a read replica, or two separate bounded contexts).
/// </summary>
public class NamedInstanceTests : IAsyncLifetime
{
    private StoveInstance _stove = null!;

    public async ValueTask InitializeAsync()
    {
        _stove = await StoveBuilder.Create()
            .WithPostgreSql("orders", opts =>
            {
                opts.MigrationSql.Add("CREATE TABLE orders (id SERIAL PRIMARY KEY, item TEXT NOT NULL)");
            })
            .WithPostgreSql("inventory", opts =>
            {
                opts.MigrationSql.Add("CREATE TABLE items (id SERIAL PRIMARY KEY, name TEXT NOT NULL, qty INT NOT NULL)");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Should_operate_on_two_named_postgresql_instances_independently()
    {
        await _stove.Validate(async s =>
        {
            // Write to the "orders" database
            await s.PostgreSql("orders", async pg =>
            {
                await pg.ShouldExecute(
                    "INSERT INTO orders (item) VALUES ('Widget')",
                    validateAffectedRows: n => Assert.Equal(1, n));

                await pg.ShouldQueryScalar<long>(
                    "SELECT COUNT(*) FROM orders",
                    validate: count => Assert.Equal(1, count));
            });

            // Write to the "inventory" database — completely separate container
            await s.PostgreSql("inventory", async pg =>
            {
                await pg.ShouldExecute(
                    "INSERT INTO items (name, qty) VALUES ('Widget', 100)",
                    validateAffectedRows: n => Assert.Equal(1, n));

                await pg.ShouldQueryScalar<long>(
                    "SELECT COUNT(*) FROM items",
                    validate: count => Assert.Equal(1, count));
            });
        });
    }

    [Fact]
    public async Task Should_have_different_connection_strings_for_named_instances()
    {
        var orders = _stove.GetSystem<PostgreSqlSystem>("orders");
        var inventory = _stove.GetSystem<PostgreSqlSystem>("inventory");

        Assert.NotEqual(orders.ConnectionString, inventory.ConnectionString);
    }

    [Fact]
    public void Should_throw_when_accessing_unregistered_name()
    {
        Assert.Throws<Stove.Net.Core.Exceptions.SystemNotRegisteredException>(() =>
            _stove.GetSystem<PostgreSqlSystem>("nonexistent"));
    }

    public async ValueTask DisposeAsync()
    {
        await _stove.DisposeAsync();
    }
}
