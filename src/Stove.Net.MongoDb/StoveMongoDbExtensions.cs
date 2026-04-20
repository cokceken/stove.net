using Stove.Net.Core;

namespace Stove.Net.MongoDb;

/// <summary>
/// Extension methods to register and access the MongoDB system.
/// </summary>
public static class StoveMongoDbExtensions
{
    /// <summary>
    /// Register a MongoDB system (with Testcontainers) in the Stove builder.
    /// </summary>
    public static StoveBuilder WithMongoDb(
        this StoveBuilder builder,
        Action<MongoDbSystemOptions>? configure = null)
    {
        var options = new MongoDbSystemOptions();
        configure?.Invoke(options);

        var system = new MongoDbSystem(options);
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the MongoDB system in a validation block.
    /// </summary>
    public static async Task MongoDb(
        this ValidationDsl dsl,
        Func<MongoDbSystem, Task> validation)
    {
        var system = dsl.Get<MongoDbSystem>();
        await validation(system);
    }
}
