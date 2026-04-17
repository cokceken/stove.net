using Stove.Net.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Stove.Net.WireMock;

/// <summary>
/// WireMock system for Stove.Net. Manages an in-process WireMock HTTP mock server
/// for intercepting external API calls made by the application under test.
/// </summary>
public class WireMockSystem(WireMockSystemOptions options) : IPluggedSystem, IExposesConfiguration
{
    private WireMockServer? _server;

    /// <summary>
    /// The underlying WireMock server instance. Use for full access to WireMock's
    /// native fluent API (Given/RespondWith, scenarios, proxying, etc.).
    /// Available after RunAsync().
    /// </summary>
    public WireMockServer Server => _server
                                    ?? throw new InvalidOperationException(
                                        "WireMock server is not started yet.");

    /// <summary>
    /// The base URL of the WireMock server (e.g., "http://localhost:12345").
    /// Available after RunAsync().
    /// </summary>
    public string Url => Server.Url
                         ?? throw new InvalidOperationException(
                             "WireMock server URL is not available.");

    public Task RunAsync()
    {
        _server = options.Port.HasValue
            ? WireMockServer.Start(options.Port.Value)
            : WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task CleanupAsync()
    {
        if (options.ResetOnCleanup && _server != null)
        {
            _server.Reset();
        }

        return Task.CompletedTask;
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _server?.Url != null)
            return options.ConfigureExposedConfiguration(_server.Url);

        if (_server?.Url != null)
        {
            return
            [
                new KeyValuePair<string, string>("WireMock:Url", _server.Url)
            ];
        }

        return [];
    }

    // --- Stub Setup ---

    /// <summary>
    /// Set up a request/response mapping using WireMock's fluent API.
    /// Chainable — returns this WireMockSystem.
    /// </summary>
    public WireMockSystem Setup(Action<WireMockServer> configure)
    {
        configure(Server);
        return this;
    }

    /// <summary>
    /// Set up a stub: when a request matches, respond with the given response.
    /// Convenience method wrapping WireMock's Given/RespondWith.
    /// </summary>
    public WireMockSystem Stub(IRequestBuilder request, IResponseBuilder response)
    {
        Server.Given(request).RespondWith(response);
        return this;
    }

    // --- Assertions ---

    /// <summary>
    /// Assert that the WireMock server received a specific number of requests matching the path and http method.
    /// </summary>
    public WireMockSystem ShouldHaveReceived(string path, string httpMethod, int expectedCount,
        Action<string?>? validate = null)
    {
        var matching = Server.LogEntries
            .Where(e => string.Equals(e.RequestMessage?.Path, path, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.RequestMessage?.Method, httpMethod, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected WireMock to have received {expectedCount} request(s) to [{httpMethod}] '{path}', " +
                $"but received {matching.Count}. " +
                FormatReceivedSummary());
        }

        if (validate != null)
        {
            var last = matching.MaxBy(e => e.RequestMessage?.DateTime);
            validate(last?.RequestMessage?.Body);
        }

        return this;
    }

    /// <summary>
    /// Assert that the WireMock server received a specific number of requests matching the path and http method.
    /// </summary>
    public WireMockSystem ShouldHaveReceived(string path, string httpMethod, Action<string?> validate) =>
        ShouldHaveReceived(path, httpMethod, 1, validate);

    /// <summary>
    /// Assert that the WireMock server received only one request matching the path and http method.
    /// </summary>
    public WireMockSystem ShouldHaveReceived(string path, string httpMethod) =>
        ShouldHaveReceived(path, httpMethod, 1);

    /// <summary>
    /// Assert that the WireMock server did not receive any request matching the path and http method.
    /// </summary>
    public WireMockSystem ShouldNotHaveReceived(string path, string httpMethod) =>
        ShouldHaveReceived(path, httpMethod, 0);

    private string FormatReceivedSummary()
    {
        var entries = Server.LogEntries.ToList();
        if (entries.Count == 0)
            return "No requests were received by WireMock.";

        var paths = entries
            .GroupBy(e => $"{e.RequestMessage?.Method} {e.RequestMessage?.Path}")
            .Select(g => $"  {g.Key}: {g.Count()} request(s)");
        return $"Received requests:\n{string.Join("\n", paths)}";
    }

    public ValueTask DisposeAsync()
    {
        _server?.Stop();
        _server?.Dispose();
        return ValueTask.CompletedTask;
    }
}