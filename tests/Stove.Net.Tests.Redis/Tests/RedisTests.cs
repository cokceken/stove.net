using Stove.Net.Redis;
using Stove.Net.Tests.Redis.Setup;
using Xunit;

namespace Stove.Net.Tests.Redis.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.Redis system.
/// Spins up a real Redis container — no web app needed.
/// </summary>
public class RedisTests(RedisOnlyFixture fixture) : IClassFixture<RedisOnlyFixture>
{
    [Fact]
    public void Should_have_connection_string()
    {
        var system = fixture.Stove.GetSystem<RedisSystem>();
        Assert.NotNull(system.ConnectionString);
        Assert.NotEmpty(system.ConnectionString);
    }

    [Fact]
    public async Task Should_set_and_get_string_value()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Redis(async redis =>
            {
                await redis.SetAsync("greeting", "hello world");
                await redis.GetAsync("greeting", value =>
                {
                    Assert.Equal("hello world", value);
                });
            });
        });
    }

    [Fact]
    public async Task Should_set_and_get_typed_value()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Redis(async redis =>
            {
                var item = new TestItem("Widget", 42);
                await redis.SetAsync("item:1", item);
                await redis.GetAsync<TestItem>("item:1", deserialized =>
                {
                    Assert.NotNull(deserialized);
                    Assert.Equal("Widget", deserialized.Name);
                    Assert.Equal(42, deserialized.Count);
                });
            });
        });
    }

    [Fact]
    public async Task Should_assert_key_existence()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Redis(async redis =>
            {
                await redis.ShouldNotExist("missing-key");
                await redis.SetAsync("exists-key", "value");
                await redis.ShouldExist("exists-key");
            });
        });
    }

    [Fact]
    public async Task Should_delete_key()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Redis(async redis =>
            {
                await redis.SetAsync("temp-key", "temp-value");
                await redis.ShouldExist("temp-key");
                await redis.DeleteAsync("temp-key");
                await redis.ShouldNotExist("temp-key");
            });
        });
    }

    [Fact]
    public async Task Should_set_and_get_hash()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Redis(async redis =>
            {
                await redis.HashSetAsync("user:1", "name", "Alice");
                await redis.HashSetAsync("user:1", "role", "Admin");
                await redis.HashGetAllAsync("user:1", entries =>
                {
                    Assert.Equal(2, entries.Count);
                    Assert.Equal("Alice", entries["name"]);
                    Assert.Equal("Admin", entries["role"]);
                });
            });
        });
    }

    private record TestItem(string Name, int Count);
}
