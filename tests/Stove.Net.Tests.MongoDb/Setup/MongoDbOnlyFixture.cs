using Stove.Net.Core;
using Stove.Net.MongoDb;
using Xunit;

namespace Stove.Net.Tests.MongoDb.Setup;

/// <summary>
/// Fixture that boots only the MongoDB system (Testcontainer).
/// No web application is involved — tests raw MongoDB operations.
/// </summary>
public class MongoDbOnlyFixture : IAsyncLifetime
{
    public StoveInstance Stove { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Stove = await StoveBuilder.Create()
            .WithMongoDb(opts => opts.DatabaseName = "smoke_test")
            .RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Stove.DisposeAsync();
    }
}
