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
public class KafkaSystem(KafkaSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem, IReportsState
{
    private const string SystemName = "Kafka";
    private KafkaContainer? _container;
    private string? _bootstrapServers;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CapturedMessage>> _messagesByTopic = new();
    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;
    private IStoveEventEmitter? _emitter;
    private int _publishCount;
    private int _failedCount;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    /// <summary>The container's bootstrap servers address, available after RunAsync().</summary>
    public string BootstrapServers => _bootstrapServers
                                      ?? throw new InvalidOperationException("Kafka container is not started yet.");

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
            return [new KeyValuePair<string, string>("Kafka:BootstrapServers", _bootstrapServers)];

        return [];
    }

    // --- Publish ---

    public async Task<KafkaSystem> PublishAsync<T>(
        string topic, T message, string? key = null, Dictionary<string, string>? headers = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var config = new ProducerConfig { BootstrapServers = BootstrapServers };
            using var producer = new ProducerBuilder<string?, string>(config).Build();

            var value = JsonSerializer.Serialize(message);
            var kafkaMessage = new Message<string?, string> { Key = key, Value = value };

            kafkaMessage.Headers = new Headers();
            if (headers != null)
            {
                foreach (var (k, v) in headers)
                    kafkaMessage.Headers.Add(k, System.Text.Encoding.UTF8.GetBytes(v));
            }

            // Inject test ID for per-test correlation (like Kotlin Stove's X-Stove-Test-Id)
            var testId = _emitter?.CurrentTestId;
            if (!string.IsNullOrEmpty(testId))
                kafkaMessage.Headers.Add(StoveInstance.StoveTestIdHeaderName,
                    System.Text.Encoding.UTF8.GetBytes(testId));

            await producer.ProduceAsync(topic, kafkaMessage);
            producer.Flush(TimeSpan.FromSeconds(5));
            Emit("Publish", $"{topic}:{key}", value, start);
        }
        catch (Exception ex) when (EmitFailure("Publish", topic, ex, start)) { }

        return this;
    }

    // --- Message History ---

    public int CapturedMessageCount => _messagesByTopic.Values.Sum(q => q.Count);

    public IReadOnlyDictionary<string, IReadOnlyList<CapturedMessage>> GetCapturedMessages() =>
        _messagesByTopic.ToDictionary(kvp => kvp.Key, IReadOnlyList<CapturedMessage> (kvp) => kvp.Value.ToArray());

    // --- Assertions ---

    public async Task<KafkaSystem> ShouldBePublished<T>(Func<T, bool> predicate, TimeSpan? timeout = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
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
                            {
                                Emit("ShouldBePublished", typeof(T).Name, $"found on {msg.Topic}", start);
                                return this;
                            }
                        }
                        catch (JsonException) { }
                    }
                }
                await Task.Delay(pollInterval);
            }

            throw new InvalidOperationException(
                $"No message of type {typeof(T).Name} matching the predicate was found within {(timeout ?? options.AssertionTimeout).TotalSeconds}s. " +
                FormatCapturedSummary());
        }
        catch (Exception ex) when (EmitFailure("ShouldBePublished", typeof(T).Name, ex, start)) { }

        return this;
    }

    public async Task<KafkaSystem> ShouldBePublished<T>(
        string topic, Func<T, bool> predicate, TimeSpan? timeout = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
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
                            {
                                Emit("ShouldBePublished", $"{topic}:{typeof(T).Name}", "found", start);
                                return this;
                            }
                        }
                        catch (JsonException) { }
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
        catch (Exception ex) when (EmitFailure("ShouldBePublished", $"{topic}:{typeof(T).Name}", ex, start)) { }

        return this;
    }

    private string FormatCapturedSummary()
    {
        if (_messagesByTopic.IsEmpty) return "No messages were captured on any topic.";
        var topicSummaries = _messagesByTopic.Select(kvp => $"  {kvp.Key}: {kvp.Value.Count} message(s)");
        return $"Captured messages by topic:\n{string.Join("\n", topicSummaries)}";
    }

    // --- Fault Injection ---

    public async Task<KafkaSystem> StopBroker()
    {
        if (_container == null) throw new InvalidOperationException("Kafka container is not started yet.");
        await _container.StopAsync();
        return this;
    }

    public async Task<KafkaSystem> StartBroker()
    {
        if (_container == null) throw new InvalidOperationException("Kafka container is not started yet.");
        await _container.StartAsync();
        return this;
    }

    public async Task<KafkaSystem> PauseBroker()
    {
        if (_container == null) throw new InvalidOperationException("Kafka container is not started yet.");
        var result = await _container.ExecAsync(["bash", "-c", "kill -STOP 1"]);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Failed to pause Kafka broker: {result.Stderr}");
        return this;
    }

    public async Task<KafkaSystem> UnpauseBroker()
    {
        if (_container == null) throw new InvalidOperationException("Kafka container is not started yet.");
        var result = await _container.ExecAsync(["bash", "-c", "kill -CONT 1"]);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Failed to unpause Kafka broker: {result.Stderr}");
        return this;
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
                                result.Topic, result.Message.Key, result.Message.Value,
                                result.Message.Timestamp.UtcDateTime));
                        }
                    }
                    catch (ConsumeException) { }
                }
            }
            finally
            {
                consumer.Close();
            }
        }, ct);

        Task.Delay(1000, ct).Wait(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_consumerCts != null)
        {
            await _consumerCts.CancelAsync();
            if (_consumerTask != null)
            {
                try { await _consumerTask; }
                catch (OperationCanceledException) { }
            }
            _consumerCts.Dispose();
        }

        if (_container != null) await _container.DisposeAsync();
    }

    public StoveSnapshot Report()
    {
        var capturedCount = _messagesByTopic.Values.Sum(q => q.Count);
        var topicCounts = _messagesByTopic.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
        return new StoveSnapshot
        {
            System = SystemName,
            StateJson = JsonSerializer.Serialize(new
            {
                publishCount = _publishCount,
                capturedMessageCount = capturedCount,
                failedCount = _failedCount,
                topicCounts
            }),
            Summary = $"{_publishCount} published, {capturedCount} captured, {_failedCount} failed"
        };
    }

    private void Emit(string action, string? input, string? output, DateTimeOffset start)
    {
        if (action == "Publish") Interlocked.Increment(ref _publishCount);
        if (_emitter == null) return;
        var traceId = _emitter.CurrentTraceId;
        var metadata = new Dictionary<string, string> { ["messaging.system"] = "kafka" };
        if (input != null)
        {
            var colonIdx = input.IndexOf(':');
            if (colonIdx > 0)
            {
                metadata["messaging.destination"] = input[..colonIdx];
                metadata["messaging.kafka.message.key"] = input[(colonIdx + 1)..];
            }
            else
            {
                metadata["messaging.destination"] = input;
            }
        }
        _emitter.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, TraceId = traceId,
            System = SystemName, Action = action,
            Result = EntryResult.Success, Input = input, Output = output,
            Metadata = metadata
        });
        _emitter.EmitSpan(new StoveSpan
        {
            TraceId = traceId, SpanId = StoveSpan.NewSpanId(),
            ParentSpanId = _emitter.CurrentSpanId,
            OperationName = action, ServiceName = SystemName,
            Start = start, End = DateTimeOffset.UtcNow, Status = "ok"
        });
    }

    private bool EmitFailure(string action, string? input, Exception ex, DateTimeOffset start)
    {
        Interlocked.Increment(ref _failedCount);
        if (_emitter != null)
        {
            var traceId = _emitter.CurrentTraceId;
            var metadata = new Dictionary<string, string> { ["messaging.system"] = "kafka" };
            if (input != null) metadata["messaging.destination"] = input;
            _emitter.Emit(new StoveEntry
            {
                TestId = _emitter.CurrentTestId, TraceId = traceId,
                System = SystemName, Action = action,
                Result = EntryResult.Failed, Input = input, Error = ex.Message,
                Metadata = metadata
            });
            _emitter.EmitSpan(new StoveSpan
            {
                TraceId = traceId, SpanId = StoveSpan.NewSpanId(),
                ParentSpanId = _emitter.CurrentSpanId,
                OperationName = action, ServiceName = SystemName,
                Start = start, End = DateTimeOffset.UtcNow, Status = "error",
                Exception = new StoveExceptionInfo(ex.GetType().Name, ex.Message,
                    ex.StackTrace?.Split('\n') ?? [])
            });
        }
        return false;
    }

    public sealed record CapturedMessage(string Topic, string? Key, string Value, DateTime Timestamp);
}
