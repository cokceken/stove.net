using Stove.Net.PostgreSql;
using Stove.Net.Tests.PostgreSql.Setup;
using Xunit;

namespace Stove.Net.Tests.PostgreSql.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.PostgreSql system.
/// Spins up a real PostgreSQL container — no web app needed.
/// </summary>
public class PostgreSqlTests(PostgreSqlOnlyFixture fixture) : IClassFixture<PostgreSqlOnlyFixture>
{
    [Fact]
    public async Task Should_execute_insert_and_query_rows()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.PostgreSql(async pg =>
            {
                await pg.ShouldExecute(
                    "INSERT INTO products (name, price) VALUES ('Widget', 9.99)",
                    validateAffectedRows: affected => Assert.Equal(1, affected));

                await pg.ShouldQuery(
                    "SELECT id, name, price FROM products WHERE name = 'Widget'",
                    mapper: reader => new
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Price = reader.GetDecimal(2)
                    },
                    validate: results =>
                    {
                        Assert.Single(results);
                        Assert.Equal("Widget", results[0].Name);
                        Assert.Equal(9.99m, results[0].Price);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_query_scalar_value()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.PostgreSql(async pg =>
            {
                await pg.ShouldExecute(
                    "INSERT INTO products (name, price) VALUES ('Gadget', 19.99)");

                await pg.ShouldQueryScalar<long>(
                    "SELECT COUNT(*) FROM products WHERE name = 'Gadget'",
                    validate: count => Assert.True(count > 0));
            });
        });
    }

    [Fact]
    public async Task Should_support_migration_sql()
    {
        // The fixture's MigrationSql already created the 'products' table.
        // Verify the table exists by querying the information schema.
        await fixture.Stove.Validate(async s =>
        {
            await s.PostgreSql(async pg =>
            {
                await pg.ShouldQueryScalar<long>(
                    "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'products'",
                    validate: count => Assert.Equal(1, count));
            });
        });
    }

    [Fact]
    public async Task Should_set_database_to_read_only_and_back()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.PostgreSql(async pg =>
            {
                // Enable read-only mode (clears pools internally)
                await pg.SetReadOnly(true);

                // Writes should fail in a new connection
                var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await pg.ShouldExecute(
                        "INSERT INTO products (name, price) VALUES ('ReadOnlyTest', 1.00)");
                });
                Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Restore writable mode (clears pools internally)
                await pg.SetReadOnly(false);

                // Writes should work again
                await pg.ShouldExecute(
                    "INSERT INTO products (name, price) VALUES ('WritableAgain', 2.00)",
                    validateAffectedRows: affected => Assert.Equal(1, affected));
            });
        });
    }

    [Fact]
    public async Task Should_simulate_slow_query()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.PostgreSql(async pg =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await pg.SimulateSlowQuery(TimeSpan.FromSeconds(1));
                sw.Stop();
                Assert.True(sw.ElapsedMilliseconds >= 900,
                    $"Expected >= 900ms but was {sw.ElapsedMilliseconds}ms");
            });
        });
    }
}
