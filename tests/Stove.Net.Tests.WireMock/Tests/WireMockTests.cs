using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Stove.Net.WireMock;
using Stove.Net.Tests.WireMock.Setup;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Stove.Net.Tests.WireMock.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.WireMock system.
/// Starts an in-process WireMock server — no containers needed.
/// </summary>
public class WireMockTests(WireMockOnlyFixture fixture) : IClassFixture<WireMockOnlyFixture>
{
    [Fact]
    public void Should_have_server_url()
    {
        var system = fixture.Stove.GetSystem<WireMockSystem>();
        Assert.NotNull(system.Url);
        Assert.StartsWith("http", system.Url);
    }

    [Fact]
    public async Task Should_stub_and_respond()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.WireMock(async wireMock =>
            {
                wireMock.Stub(
                    Request.Create().WithPath("/api/greet").UsingGet(),
                    Response.Create().WithStatusCode(200).WithBody("hello"));

                using var client = new HttpClient();
                var response = await client.GetStringAsync($"{wireMock.Url}/api/greet");
                Assert.Equal("hello", response);
            });
        });
    }

    [Fact]
    public async Task Should_assert_received_request()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.WireMock(async wireMock =>
            {
                wireMock.Stub(
                    Request.Create().WithPath("/api/notify").UsingPost(),
                    Response.Create().WithStatusCode(202));

                using var client = new HttpClient();
                await client.PostAsync($"{wireMock.Url}/api/notify",
                    new StringContent("{\"msg\":\"test\"}", System.Text.Encoding.UTF8, "application/json"));

                wireMock.ShouldHaveReceived("/api/notify", HttpMethods.Post);
                wireMock.ShouldHaveReceived("/api/notify", HttpMethods.Post, 1);
                wireMock.ShouldHaveReceived("/api/notify", HttpMethods.Post, 1, _ => { });
            });
        });
    }

    [Fact]
    public async Task Should_assert_not_received()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.WireMock(async wireMock =>
            {
                wireMock.ShouldNotHaveReceived("/api/never-called", HttpMethods.Get);
            });
        });
    }

    [Fact]
    public async Task Should_assert_request_body()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.WireMock(async wireMock =>
            {
                wireMock.Stub(
                    Request.Create().WithPath("/api/data").UsingPost(),
                    Response.Create().WithStatusCode(200));

                var payload = new { Name = "Widget", Count = 5 };
                using var client = new HttpClient();
                await client.PostAsJsonAsync($"{wireMock.Url}/api/data", payload);

                wireMock.ShouldHaveReceived("/api/data", HttpMethods.Post, body =>
                {
                    Assert.NotNull(body);
                    var doc = JsonDocument.Parse(body);
                    Assert.Equal("Widget", doc.RootElement.GetProperty("name").GetString());
                    Assert.Equal(5, doc.RootElement.GetProperty("count").GetInt32());
                });
            });
        });
    }

    [Fact]
    public async Task Should_expose_server_for_native_api()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.WireMock(async wireMock =>
            {
                // Use native WireMock API directly
                wireMock.Server
                    .Given(Request.Create().WithPath("/native").UsingGet())
                    .RespondWith(Response.Create().WithStatusCode(200).WithBody("native-api"));

                using var client = new HttpClient();
                var response = await client.GetStringAsync($"{wireMock.Url}/native");
                Assert.Equal("native-api", response);
            });
        });
    }
}