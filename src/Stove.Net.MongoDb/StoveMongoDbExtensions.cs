using Stove.Net.Core;

namespace Stove.Net.MongoDb;

/// <summary>
/// Extension methods to register and access the MongoDB system.
/// </summary>
public static class StoveMongoDbExtensions
{
    extension(StoveBuilder builder)
    {
        /// <summary>
        /// Register a MongoDB system (with Testcontainers) in the Stove builder.
        /// </summary>
        public StoveBuilder WithMongoDb(Action<MongoDbSystemOptions>? configure = null) =>
            builder.WithMongoDb(SystemKey.DefaultName, configure);

        /// <summary>
        /// Register a named MongoDB system. Use when you need multiple MongoDB instances.
        /// </summary>
        public StoveBuilder WithMongoDb(string name,
            Action<MongoDbSystemOptions>? configure = null)
        {
            var options = new MongoDbSystemOptions();
            configure?.Invoke(options);
            builder.WithSystem(new MongoDbSystem(options), name);
            return builder;
        }
    }

    extension(ValidationDsl dsl)
    {
        /// <summary>
        /// Access the default MongoDB system in a validation block.
        /// </summary>
        public async Task MongoDb(Func<MongoDbSystem, Task> validation)
        {
            await validation(dsl.Get<MongoDbSystem>());
        }

        /// <summary>
        /// Access a named MongoDB system in a validation block.
        /// </summary>
        public async Task MongoDb(string name, Func<MongoDbSystem, Task> validation)
        {
            await validation(dsl.Get<MongoDbSystem>(name));
        }
    }
}