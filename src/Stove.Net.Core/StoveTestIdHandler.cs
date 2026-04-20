using System.Diagnostics;

namespace Stove.Net.Core;

/// <summary>
/// HTTP message handler that injects the current Stove test ID into outgoing requests.
/// Reads the test ID from <see cref="Activity.Current"/> baggage (key: <c>stove.test.id</c>)
/// and adds it as the <c>X-Stove-Test-Id</c> header. Also propagates the test ID via
/// the W3C <c>baggage</c> header for cross-service trace correlation.
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
        {
            request.Headers.TryAddWithoutValidation(StoveInstance.StoveTestIdHeaderName, testId);
            // W3C baggage propagation for cross-service correlation
            request.Headers.TryAddWithoutValidation("baggage",
                $"{StoveInstance.StoveTestIdBaggageKey}={testId}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
