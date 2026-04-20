using System.Diagnostics;

namespace Stove.Net.Core;

/// <summary>
/// HTTP message handler that injects the current Stove test ID into outgoing requests.
/// Reads the test ID from <see cref="Activity.Current"/> baggage (key: <c>stove.test.id</c>)
/// and adds it as the <c>X-Stove-Test-Id</c> header. This enables per-test correlation
/// in downstream services, WireMock, and the Stove dashboard.
///
/// Register via <c>HttpClientBuilder.AddHttpMessageHandler&lt;StoveTestIdHandler&gt;()</c>
/// or use the Stove builder extension.
/// </summary>
public sealed class StoveTestIdHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var testId = Activity.Current?.GetBaggageItem(StoveInstance.StoveTestIdBaggageKey);
        if (!string.IsNullOrEmpty(testId))
            request.Headers.TryAddWithoutValidation(StoveInstance.StoveTestIdHeaderName, testId);

        return base.SendAsync(request, cancellationToken);
    }
}
