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
        builder.WithSystem(new KafkaSystem(options));
        return builder;
    }

    /// <summary>
    /// Register a named Kafka system. Use when you need multiple Kafka clusters.
    /// </summary>
    public static StoveBuilder WithKafka(
        this StoveBuilder builder,
        string name,
        Action<KafkaSystemOptions>? configure = null)
    {
        var options = new KafkaSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new KafkaSystem(options), name);
        return builder;
    }

    /// <summary>
    /// Access the default Kafka system in a validation block.
    /// </summary>
    public static async Task Kafka(this ValidationDsl dsl, Func<KafkaSystem, Task> validation)
    {
        await validation(dsl.Get<KafkaSystem>());
    }

    /// <summary>
    /// Access a named Kafka system in a validation block.
    /// </summary>
    public static async Task Kafka(this ValidationDsl dsl, string name, Func<KafkaSystem, Task> validation)
    {
        await validation(dsl.Get<KafkaSystem>(name));
    }
}
