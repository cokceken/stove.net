using Stove.Net.Core;
using Stove.Net.Kafka;
using Xunit;

namespace Stove.Net.Tests.Kafka.Setup;

/// <summary>
/// Fixture that boots only the Kafka system (Testcontainer).
/// No web application is involved — tests raw Kafka operations.
/// </summary>
public class KafkaOnlyFixture : IAsyncLifetime
{
    public StoveInstance Stove { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Stove = await StoveBuilder.Create()
            .WithKafka(opts =>
            {
                opts.TopicsToConsume.Add("test-topic");
            })
            .RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Stove.DisposeAsync();
    }
}
