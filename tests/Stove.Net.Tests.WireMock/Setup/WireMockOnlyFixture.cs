using Stove.Net.Core;
using Stove.Net.WireMock;
using Xunit;

namespace Stove.Net.Tests.WireMock.Setup;

/// <summary>
/// Fixture that boots only the WireMock system (in-process HTTP mock server).
/// No web application or containers are involved — tests raw WireMock operations.
/// </summary>
public class WireMockOnlyFixture : IAsyncLifetime
{
    public StoveInstance Stove { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Stove = await StoveBuilder.Create()
            .WithWireMock()
            .RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Stove.DisposeAsync();
    }
}
