using System.Net;
using Microsoft.AspNetCore.Http;
using Stove.Net.Http;
using Stove.Net.Kafka;
using Stove.Net.PostgreSql;
using Stove.Net.Redis;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Tests.Integration.Setup;
using Stove.Net.WireMock;
using Xunit;

namespace Stove.Net.Tests.Integration.Tests;

/// <summary>
/// Integration tests combining HTTP + PostgreSQL + Kafka + Redis systems.
/// Validates end-to-end flows through a real API, database, message broker, and cache.
/// </summary>
public class OrderTests(IntegrationFixture fixture) : IClassFixture<IntegrationFixture>
{
    [Fact]
    public async Task Should_create_order_persist_to_database_publish_event_and_cache()
    {
        await fixture.Stove.Validate(async s =>
        {
            Order? createdOrder = null;

            await s.Http(async http =>
            {
                await http.PostAsync<Order>("/api/orders",
                    body: new CreateOrderRequest("Widget", 5),
                    validate: order =>
                    {
                        createdOrder = order;
                        Assert.Equal("Confirmed", order.Status);
                    });
            });

            Assert.NotNull(createdOrder);

            await s.PostgreSql(async pg =>
            {
                await pg.ShouldQuery(
                    "SELECT id, product_name, quantity, status FROM orders WHERE product_name = 'Widget'",
                    mapper: reader => new Order
                    {
                        Id = reader.GetInt32(0),
                        ProductName = reader.GetString(1),
                        Quantity = reader.GetInt32(2),
                        Status = reader.GetString(3)
                    },
                    validate: results =>
                    {
                        Assert.Single(results);
                        Assert.Equal(5, results[0].Quantity);
                        Assert.Equal("Confirmed", results[0].Status);
                    });
            });

            await s.Kafka(async kafka =>
            {
                await kafka.ShouldBePublished<OrderCreatedEvent>(
                    "order-events",
                    e => e.ProductName == "Widget" && e.Quantity == 5);
            });

            await s.Redis(async redis =>
            {
                await redis.GetAsync<Order>($"order:{createdOrder!.Id}", cached =>
                {
                    Assert.NotNull(cached);
                    Assert.Equal("Widget", cached.ProductName);
                    Assert.Equal(5, cached.Quantity);
                });
            });

            await s.WireMock(async wireMock =>
            {
                wireMock.ShouldHaveReceived("/api/notifications", HttpMethods.Post);
            });
        });
    }

    [Fact]
    public async Task Should_return_404_for_nonexistent_order()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Http(async http =>
            {
                await http.GetAsync("/api/orders/99999",
                    validate: response =>
                    {
                        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_get_order_by_id()
    {
        await fixture.Stove.Validate(async s =>
        {
            Order? createdOrder = null;

            await s.Http(async http =>
            {
                await http.PostAsync<Order>("/api/orders",
                    body: new CreateOrderRequest("Gadget", 3),
                    validate: order =>
                    {
                        createdOrder = order;
                        Assert.Equal("Gadget", order.ProductName);
                        Assert.Equal("Confirmed", order.Status);
                    });
            });

            Assert.NotNull(createdOrder);

            await s.Http(async http =>
            {
                await http.GetAsync<Order>($"/api/orders/{createdOrder!.Id}",
                    validate: order =>
                    {
                        Assert.Equal("Gadget", order.ProductName);
                        Assert.Equal(3, order.Quantity);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_remove_cached_order_on_delete()
    {
        await fixture.Stove.Validate(async s =>
        {
            Order? createdOrder = null;

            await s.Http(async http =>
            {
                await http.PostAsync<Order>("/api/orders",
                    body: new CreateOrderRequest("Disposable", 1),
                    validate: order => { createdOrder = order; });
            });

            Assert.NotNull(createdOrder);

            // Verify cached
            await s.Redis(async redis =>
            {
                await redis.ShouldExist($"order:{createdOrder!.Id}");
            });

            // Delete the order
            await s.Http(async http =>
            {
                await http.DeleteAsync($"/api/orders/{createdOrder!.Id}",
                    validate: response =>
                    {
                        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                    });
            });

            // Verify cache evicted
            await s.Redis(async redis =>
            {
                await redis.ShouldNotExist($"order:{createdOrder!.Id}");
            });
        });
    }
}
