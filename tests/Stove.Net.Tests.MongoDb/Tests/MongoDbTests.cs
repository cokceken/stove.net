using MongoDB.Driver;
using Stove.Net.MongoDb;
using Stove.Net.Tests.MongoDb.Setup;
using Xunit;

namespace Stove.Net.Tests.MongoDb.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.MongoDb system.
/// Spins up a real MongoDB container — no web app needed.
/// </summary>
public class MongoDbTests(MongoDbOnlyFixture fixture) : IClassFixture<MongoDbOnlyFixture>
{
    [Fact]
    public void Should_have_connection_string()
    {
        var system = fixture.Stove.GetSystem<MongoDbSystem>();
        Assert.NotNull(system.ConnectionString);
        Assert.NotEmpty(system.ConnectionString);
    }

    [Fact]
    public async Task Should_insert_and_find_document()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                var product = new TestProduct { Name = "Widget", Price = 9.99m };
                await mongo.InsertAsync("products", product);

                await mongo.ShouldFind<TestProduct>("products",
                    p => p.Name == "Widget",
                    results =>
                    {
                        Assert.Single(results);
                        Assert.Equal(9.99m, results[0].Price);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_insert_many_and_find_all()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                var items = new[]
                {
                    new TestProduct { Name = "A", Price = 1.0m },
                    new TestProduct { Name = "B", Price = 2.0m },
                    new TestProduct { Name = "C", Price = 3.0m }
                };

                await mongo.InsertManyAsync("bulk_products", items);

                await mongo.ShouldFindAll<TestProduct>("bulk_products", results =>
                {
                    Assert.Equal(3, results.Count);
                });
            });
        });
    }

    [Fact]
    public async Task Should_assert_existence()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                await mongo.ShouldNotExist<TestProduct>("existence_test",
                    p => p.Name == "Ghost");

                await mongo.InsertAsync("existence_test",
                    new TestProduct { Name = "Real", Price = 5.0m });

                await mongo.ShouldExist<TestProduct>("existence_test",
                    p => p.Name == "Real");
            });
        });
    }

    [Fact]
    public async Task Should_delete_documents()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                await mongo.InsertAsync("delete_test",
                    new TestProduct { Name = "ToDelete", Price = 1.0m });

                await mongo.ShouldExist<TestProduct>("delete_test",
                    p => p.Name == "ToDelete");

                await mongo.DeleteAsync<TestProduct>("delete_test",
                    p => p.Name == "ToDelete");

                await mongo.ShouldNotExist<TestProduct>("delete_test",
                    p => p.Name == "ToDelete");
            });
        });
    }

    [Fact]
    public async Task Should_count_documents()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                await mongo.InsertManyAsync("count_test", new[]
                {
                    new TestProduct { Name = "X", Price = 10m },
                    new TestProduct { Name = "X", Price = 20m },
                    new TestProduct { Name = "Y", Price = 30m }
                });

                await mongo.ShouldCount<TestProduct>("count_test",
                    p => p.Name == "X",
                    count => Assert.Equal(2, count));
            });
        });
    }

    [Fact]
    public async Task Should_drop_collection()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.MongoDb(async mongo =>
            {
                await mongo.InsertAsync("drop_test",
                    new TestProduct { Name = "Temp", Price = 0m });

                await mongo.ShouldExist<TestProduct>("drop_test",
                    p => p.Name == "Temp");

                await mongo.DropCollectionAsync("drop_test");

                await mongo.ShouldNotExist<TestProduct>("drop_test",
                    p => p.Name == "Temp");
            });
        });
    }

    public class TestProduct
    {
        public MongoDB.Bson.ObjectId Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }
}
