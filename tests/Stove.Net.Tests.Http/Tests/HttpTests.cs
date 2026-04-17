using System.Net;
using Stove.Net.Http;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Tests.Http.Setup;
using Xunit;

namespace Stove.Net.Tests.Http.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.Http system.
/// Uses an in-memory database — no containers required.
/// </summary>
public class HttpTests(HttpOnlyFixture fixture) : IClassFixture<HttpOnlyFixture>
{
    [Fact]
    public async Task Should_get_health_endpoint()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Http(async http =>
            {
                await http.GetAsync("/health",
                    validate: response =>
                    {
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_post_and_retrieve_resource()
    {
        await fixture.Stove.Validate(async s =>
        {
            // Extract the created order via the validate callback
            Order? created = null;

            await s.Http(async http =>
            {
                await http.PostAsync<Order>("/api/orders",
                    body: new CreateOrderRequest("HttpTestProduct", 2),
                    validate: order =>
                    {
                        created = order;
                        Assert.Equal("HttpTestProduct", order.ProductName);
                        Assert.Equal(2, order.Quantity);
                    });
            });

            Assert.NotNull(created);

            await s.Http(async http =>
            {
                await http.GetAsync<Order>($"/api/orders/{created!.Id}",
                    validate: order =>
                    {
                        Assert.Equal("HttpTestProduct", order.ProductName);
                    });
            });
        });
    }

    [Fact]
    public async Task Should_return_404_for_missing_resource()
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
}
