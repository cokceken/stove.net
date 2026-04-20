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
        builder.WithSystem(new MongoDbSystem(options));
        return builder;
    }

    /// <summary>
    /// Register a named MongoDB system. Use when you need multiple MongoDB instances.
    /// </summary>
    public static StoveBuilder WithMongoDb(
        this StoveBuilder builder,
        string name,
        Action<MongoDbSystemOptions>? configure = null)
    {
        var options = new MongoDbSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new MongoDbSystem(options), name);
        return builder;
    }

    /// <summary>
    /// Access the default MongoDB system in a validation block.
    /// </summary>
    public static async Task MongoDb(this ValidationDsl dsl, Func<MongoDbSystem, Task> validation)
    {
        await validation(dsl.Get<MongoDbSystem>());
    }

    /// <summary>
    /// Access a named MongoDB system in a validation block.
    /// </summary>
    public static async Task MongoDb(this ValidationDsl dsl, string name, Func<MongoDbSystem, Task> validation)
    {
        await validation(dsl.Get<MongoDbSystem>(name));
    }
}
