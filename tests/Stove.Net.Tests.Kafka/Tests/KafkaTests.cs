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

    private record TestMessage(string Text, int Number);
}
