using Stove.Net.Core;

namespace Stove.Net.Kafka;

/// <summary>
/// Extension methods to register and access the Kafka system.
/// </summary>
public static class StoveKafkaExtensions
{
    extension(StoveBuilder builder)
    {
        /// <summary>
        /// Register a Kafka system (with Testcontainers) in the Stove builder.
        /// </summary>
        public StoveBuilder WithKafka(Action<KafkaSystemOptions>? configure = null) =>
            builder.WithKafka(SystemKey.DefaultName, configure);

        /// <summary>
        /// Register a named Kafka system. Use when you need multiple Kafka clusters.
        /// </summary>
        public StoveBuilder WithKafka(string name,
            Action<KafkaSystemOptions>? configure = null)
        {
            var options = new KafkaSystemOptions();
            configure?.Invoke(options);
            builder.WithSystem(new KafkaSystem(options), name);
            return builder;
        }
    }

    extension(ValidationDsl dsl)
    {
        /// <summary>
        /// Access the default Kafka system in a validation block.
        /// </summary>
        public async Task Kafka(Func<KafkaSystem, Task> validation)
        {
            await validation(dsl.Get<KafkaSystem>());
        }

        /// <summary>
        /// Access a named Kafka system in a validation block.
        /// </summary>
        public async Task Kafka(string name, Func<KafkaSystem, Task> validation)
        {
            await validation(dsl.Get<KafkaSystem>(name));
        }
    }
}