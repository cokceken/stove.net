using Stove.Net.Kafka;
using Stove.Net.Tests.Kafka.Setup;
using Xunit;

namespace Stove.Net.Tests.Kafka.Tests;

/// <summary>
/// Smoke tests for the Stove.Net.Kafka system.
/// Spins up a real Kafka container — no web app needed.
/// </summary>
public class KafkaTests(KafkaOnlyFixture fixture) : IClassFixture<KafkaOnlyFixture>
{
    [Fact]
    public void Should_have_bootstrap_servers()
    {
        var system = fixture.Stove.GetSystem<KafkaSystem>();
        Assert.NotNull(system.BootstrapServers);
        Assert.NotEmpty(system.BootstrapServers);
    }

    [Fact]
    public async Task Should_publish_and_capture_message()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Kafka(async kafka =>
            {
                await kafka.PublishAsync("test-topic", new TestMessage("hello", 42));

                await kafka.ShouldBePublished<TestMessage>(msg =>
                    msg.Text == "hello" && msg.Number == 42);
            });
        });
    }

    [Fact]
    public async Task Should_publish_and_filter_by_topic()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Kafka(async kafka =>
            {
                await kafka.PublishAsync("test-topic", new TestMessage("topic-filter", 99));

                await kafka.ShouldBePublished<TestMessage>(
                    "test-topic",
                    msg => msg.Text == "topic-filter");
            });
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Should_pause_and_unpause_broker()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Kafka(async kafka =>
            {
                // Verify publishing works before pause
                await kafka.PublishAsync("test-topic", new TestMessage("before-pause", 0));

                // Pause the broker — freezes all processes (connections will hang)
                await kafka.PauseBroker();

                // Wait briefly then unpause
                await Task.Delay(2000);

                // Unpause the broker — resumes all processes
                await kafka.UnpauseBroker();

                // Give the broker a moment to stabilize
                await Task.Delay(3000);

                // Publishing should work again after unpausing
                await kafka.PublishAsync("test-topic", new TestMessage("after-unpause", 1));
            });
        });
    }

    private record TestMessage(string Text, int Number);
}
