using Stove.Net.Core;
using Stove.Net.Redis;
using Xunit;

namespace Stove.Net.Tests.Redis.Setup;

/// <summary>
/// Fixture that boots only the Redis system (Testcontainer).
/// No web application is involved — tests raw Redis operations.
/// </summary>
public class RedisOnlyFixture : IAsyncLifetime
{
    public StoveInstance Stove { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Stove = await StoveBuilder.Create()
            .WithRedis()
            .RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Stove.DisposeAsync();
    }
}
