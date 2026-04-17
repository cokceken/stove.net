using Stove.Net.Core;

namespace Stove.Net.Kafka;

/// <summary>
/// Extension methods to register and access the Kafka system.
/// </summary>
public static class StoveKafkaExtensions
{
    /// <summary>
    /// Register a Kafka system (with Testcontainers) in the Stove builder.
    /// </summary>
    public static StoveBuilder WithKafka(
        this StoveBuilder builder,
        Action<KafkaSystemOptions>? configure = null)
    {
        var options = new KafkaSystemOptions();
        configure?.Invoke(options);

        var system = new KafkaSystem(options);
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the Kafka system in a validation block.
    /// </summary>
    public static async Task Kafka(
        this ValidationDsl dsl,
        Func<KafkaSystem, Task> validation)
    {
        var system = dsl.Get<KafkaSystem>();
        await validation(system);
    }
}
