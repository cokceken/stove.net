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
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CapturedMessage>> _messagesByTopic = new();
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

    // --- Message History ---

    /// <summary>
    /// Returns the total number of captured messages across all topics.
    /// </summary>
    public int CapturedMessageCount => _messagesByTopic.Values.Sum(q => q.Count);

    /// <summary>
    /// Returns a snapshot of all captured messages grouped by topic.
    /// Useful for diagnostics and debugging failed assertions.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CapturedMessage>> GetCapturedMessages() =>
        _messagesByTopic.ToDictionary(
            kvp => kvp.Key, IReadOnlyList<CapturedMessage> (kvp) => kvp.Value.ToArray());

    // --- Assertions ---

    /// <summary>
    /// Assert that a message matching the predicate was published to any consumed topic.
    /// The background consumer captures all messages into a topic-keyed history;
    /// this method polls the history until a match is found or the timeout expires.
    /// </summary>
    public async Task<KafkaSystem> ShouldBePublished<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? options.AssertionTimeout);
        var pollInterval = TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            foreach (var queue in _messagesByTopic.Values)
            {
                foreach (var msg in queue)
                {
                    try
                    {
                        var deserialized = JsonSerializer.Deserialize<T>(msg.Value);
                        if (deserialized != null && predicate(deserialized))
                            return this;
                    }
                    catch (JsonException)
                    {
                        // Message doesn't deserialize to T — continue scanning
                    }
                }
            }

            await Task.Delay(pollInterval);
        }

        throw new InvalidOperationException(
            $"No message of type {typeof(T).Name} matching the predicate was found within {(timeout ?? options.AssertionTimeout).TotalSeconds}s. " +
            FormatCapturedSummary());
    }

    /// <summary>
    /// Assert that a message matching the predicate was published to a specific topic.
    /// Only scans messages captured on the given topic (O(1) topic lookup).
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
            if (_messagesByTopic.TryGetValue(topic, out var queue))
            {
                foreach (var msg in queue)
                {
                    try
                    {
                        var deserialized = JsonSerializer.Deserialize<T>(msg.Value);
                        if (deserialized != null && predicate(deserialized))
                            return this;
                    }
                    catch (JsonException)
                    {
                        // Message doesn't deserialize to T — continue scanning
                    }
                }
            }

            await Task.Delay(pollInterval);
        }

        var topicCount = _messagesByTopic.TryGetValue(topic, out var q) ? q.Count : 0;
        throw new InvalidOperationException(
            $"No message of type {typeof(T).Name} matching the predicate was found on topic '{topic}' within {(timeout ?? options.AssertionTimeout).TotalSeconds}s. " +
            $"Topic '{topic}' has {topicCount} message(s). " +
            FormatCapturedSummary());
    }

    private string FormatCapturedSummary()
    {
        if (_messagesByTopic.IsEmpty)
            return "No messages were captured on any topic.";

        var topicSummaries = _messagesByTopic.Select(kvp => $"  {kvp.Key}: {kvp.Value.Count} message(s)");
        return $"Captured messages by topic:\n{string.Join("\n", topicSummaries)}";
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
                            var queue = _messagesByTopic.GetOrAdd(result.Topic, _ => new ConcurrentQueue<CapturedMessage>());
                            queue.Enqueue(new CapturedMessage(
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

    public sealed record CapturedMessage(string Topic, string? Key, string Value, DateTime Timestamp);
}