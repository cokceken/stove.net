using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using DotNet.Testcontainers.Images;
using Stove.Net.Core;
using Testcontainers.Kafka;

namespace Stove.Net.Kafka;

/// <summary>
/// Kafka system using Testcontainers. Manages a Kafka container,
/// a background consumer for capturing published messages, and provides
/// publish/assertion methods.
/// </summary>
public class KafkaSystem(KafkaSystemOptions options) : IPluggedSystem, IExposesConfiguration
{
    private KafkaContainer? _container;
    private string? _bootstrapServers;
    private readonly ConcurrentBag<CapturedMessage> _capturedMessages = new();
    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;

    /// <summary>
    /// The container's bootstrap servers address, available after RunAsync().
    /// </summary>
    public string BootstrapServers => _bootstrapServers
                                      ?? throw new InvalidOperationException(
                                          "Kafka container is not started yet.");

    public async Task RunAsync()
    {
        _container = new KafkaBuilder(new DockerImage("confluentinc/cp-kafka:7.8.0")).Build();
        await _container.StartAsync();
        _bootstrapServers = _container.GetBootstrapAddress();

        if (options.TopicsToConsume.Count > 0)
            StartBackgroundConsumer();
    }

    public async Task CleanupAsync()
    {
        if (options.Cleanup != null)
            await options.Cleanup();
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _bootstrapServers != null)
            return options.ConfigureExposedConfiguration(_bootstrapServers);

        if (_bootstrapServers != null)
        {
            return
            [
                new KeyValuePair<string, string>("Kafka:BootstrapServers", _bootstrapServers)
            ];
        }

        return [];
    }

    // --- Publish ---

    /// <summary>
    /// Publish a message to a Kafka topic.
    /// </summary>
    public async Task<KafkaSystem> PublishAsync<T>(
        string topic,
        T message,
        string? key = null,
        Dictionary<string, string>? headers = null)
    {
        var config = new ProducerConfig { BootstrapServers = BootstrapServers };
        using var producer = new ProducerBuilder<string?, string>(config).Build();

        var value = JsonSerializer.Serialize(message);
        var kafkaMessage = new Message<string?, string>
        {
            Key = key,
            Value = value
        };

        if (headers != null)
        {
            kafkaMessage.Headers = new Headers();
            foreach (var (k, v) in headers)
                kafkaMessage.Headers.Add(k, System.Text.Encoding.UTF8.GetBytes(v));
        }

        await producer.ProduceAsync(topic, kafkaMessage);
        producer.Flush(TimeSpan.FromSeconds(5));

        return this;
    }

    // --- Assertions ---

    /// <summary>
    /// Assert that a message matching the predicate was published to a consumed topic.
    /// The background consumer captures messages; this method polls until a match is found
    /// or the timeout expires.
    /// </summary>
    public async Task<KafkaSystem> ShouldBePublished<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? options.AssertionTimeout);
        var pollInterval = TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            foreach (var msg in _capturedMessages)
            {
                try
                {
                    var deserialized = JsonSerializer.Deserialize<T>(msg.Value);
                    if (deserialized != null && predicate(deserialized))
                        return this;
                }
                catch (JsonException)
                {
                    // Message doesn't match type — skip
                }
            }

            await Task.Delay(pollInterval);
        }

        throw new InvalidOperationException(
            $"No message of type {typeof(T).Name} matching the predicate was found within {(timeout ?? options.AssertionTimeout).TotalSeconds}s. " +
            $"Captured {_capturedMessages.Count} message(s) total.");
    }

    /// <summary>
    /// Assert that a message matching the predicate was published to a specific topic.
    /// </summary>
    public async Task<KafkaSystem> ShouldBePublished<T>(
        string topic,
        Func<T, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? options.AssertionTimeout);
        var pollInterval = TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            foreach (var msg in _capturedMessages.Where(m => m.Topic == topic))
            {
                try
                {
                    var deserialized = JsonSerializer.Deserialize<T>(msg.Value);
                    if (deserialized != null && predicate(deserialized))
                        return this;
                }
                catch (JsonException)
                {
                    // Message doesn't match type — skip
                }
            }

            await Task.Delay(pollInterval);
        }

        throw new InvalidOperationException(
            $"No message of type {typeof(T).Name} matching the predicate was found on topic '{topic}' within {(timeout ?? options.AssertionTimeout).TotalSeconds}s. " +
            $"Captured {_capturedMessages.Count(m => m.Topic == topic)} message(s) on this topic.");
    }

    // --- Background Consumer ---

    private void StartBackgroundConsumer()
    {
        _consumerCts = new CancellationTokenSource();
        var ct = _consumerCts.Token;

        _consumerTask = Task.Run(async () =>
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = $"stove-consumer-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            };

            using var consumer = new ConsumerBuilder<string?, string>(config).Build();
            consumer.Subscribe(options.TopicsToConsume);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                        if (result?.Message?.Value != null)
                        {
                            _capturedMessages.Add(new CapturedMessage(
                                result.Topic,
                                result.Message.Key,
                                result.Message.Value,
                                result.Message.Timestamp.UtcDateTime));
                        }
                    }
                    catch (ConsumeException)
                    {
                        // Transient error — keep consuming
                    }
                }
            }
            finally
            {
                consumer.Close();
            }
        }, ct);

        // Give the consumer time to subscribe and start polling
        Task.Delay(1000, ct).Wait(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_consumerCts != null)
        {
            await _consumerCts.CancelAsync();
            if (_consumerTask != null)
            {
                try
                {
                    await _consumerTask;
                }
                catch (OperationCanceledException)
                {
                    //ignore
                }
            }

            _consumerCts.Dispose();
        }

        if (_container != null)
            await _container.DisposeAsync();
    }

    private sealed record CapturedMessage(string Topic, string? Key, string Value, DateTime Timestamp);
}