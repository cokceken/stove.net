using System.Text.Json;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using WireMock.Admin.Mappings;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using FaultType = WireMock.ResponseBuilders.FaultType;

namespace Stove.Net.WireMock;

/// <summary>
/// WireMock system for Stove.Net. Manages an in-process WireMock HTTP mock server
/// for intercepting external API calls made by the application under test.
/// </summary>
public class WireMockSystem(WireMockSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem, IReportsState
{
    private const string SystemName = "WireMock";
    private WireMockServer? _server;
    private IStoveEventEmitter? _emitter;
    private int _assertionCount;
    private int _failedCount;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    public WireMockServer Server => _server
                                    ?? throw new InvalidOperationException("WireMock server is not started yet.");

    public string Url => Server.Url
                         ?? throw new InvalidOperationException("WireMock server URL is not available.");

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
            _server.ResetLogEntries();
        return Task.CompletedTask;
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _server?.Url != null)
            return options.ConfigureExposedConfiguration(_server.Url);

        if (_server?.Url != null)
            return [new KeyValuePair<string, string>("WireMock:Url", _server.Url)];

        return [];
    }

    // --- Stub Setup ---

    public WireMockSystem Setup(Action<WireMockServer> configure)
    {
        configure(Server);
        return this;
    }

    public WireMockSystem Stub(IRequestBuilder request, IResponseBuilder response)
    {
        Server.Given(request).RespondWith(response);
        return this;
    }

    // --- Fault / Delay Stubs ---

    public WireMockSystem StubWithDelay(string path, string httpMethod, int statusCode, TimeSpan delay, string? body = null)
    {
        Server.WithMapping(new MappingModel
        {
            Request = new RequestModel { Path = path, Methods = [httpMethod] },
            Response = new ResponseModel { StatusCode = statusCode, Body = body, Delay = (int)delay.TotalMilliseconds }
        });
        return this;
    }

    public WireMockSystem StubWithRandomDelay(string path, string httpMethod, int statusCode,
        TimeSpan minDelay, TimeSpan maxDelay, string? body = null)
    {
        Server.WithMapping(new MappingModel
        {
            Request = new RequestModel { Path = path, Methods = [httpMethod] },
            Response = new ResponseModel
            {
                StatusCode = statusCode, Body = body,
                MinimumRandomDelay = (int)minDelay.TotalMilliseconds,
                MaximumRandomDelay = (int)maxDelay.TotalMilliseconds
            }
        });
        return this;
    }

    public WireMockSystem StubFault(string path, string httpMethod, FaultType faultType)
    {
        Server.WithMapping(new MappingModel
        {
            Request = new RequestModel { Path = path, Methods = [httpMethod] },
            Response = new ResponseModel { Fault = new FaultModel { Type = faultType.ToString() } }
        });
        return this;
    }

    // --- Assertions ---

    /// <summary>
    /// Core assertion: validates count and optionally inspects the request body of the last matching request.
    /// All other ShouldHaveReceived overloads delegate to this method.
    /// </summary>
    public WireMockSystem ShouldHaveReceivedBody(string path, string? httpMethod, Action<string?>? validate,
        int expectedCount = 1)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var matching = Server.LogEntries
                .Where(e => string.Equals(e.RequestMessage?.Path, path, StringComparison.OrdinalIgnoreCase) &&
                            (httpMethod == null ||
                             string.Equals(e.RequestMessage?.Method, httpMethod, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matching.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected WireMock to have received {expectedCount} request(s) to [{httpMethod ?? "*"}] '{path}', " +
                    $"but received {matching.Count}. " + FormatReceivedSummary());
            }

            if (validate != null)
            {
                var last = matching.MaxBy(e => e.RequestMessage?.DateTime);
                validate(last?.RequestMessage?.Body);
            }

            Emit("ShouldHaveReceived", $"{httpMethod} {path}", $"{matching.Count} request(s)", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldHaveReceived", $"{httpMethod} {path}", ex, start)) { }

        return this;
    }

    public WireMockSystem ShouldHaveReceived(string path, string httpMethod, int expectedCount,
        Action<string?>? validate = null) =>
        ShouldHaveReceivedBody(path, httpMethod, validate, expectedCount);

    public WireMockSystem ShouldHaveReceived(string path, string httpMethod, Action<string?> validate) =>
        ShouldHaveReceivedBody(path, httpMethod, validate, 1);

    public WireMockSystem ShouldHaveReceived(string path, string httpMethod) =>
        ShouldHaveReceivedBody(path, httpMethod, null, 1);

    public WireMockSystem ShouldNotHaveReceived(string path, string httpMethod) =>
        ShouldHaveReceivedBody(path, httpMethod, null, 0);

    private string FormatReceivedSummary()
    {
        var entries = Server.LogEntries.ToList();
        if (entries.Count == 0) return "No requests were received by WireMock.";
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

    public StoveSnapshot Report()
    {
        var stubCount = _server?.Mappings.Count() ?? 0;
        var requestLogCount = _server?.LogEntries.Count() ?? 0;
        return new StoveSnapshot
        {
            System = SystemName,
            StateJson = JsonSerializer.Serialize(new
            {
                stubCount,
                requestLogCount,
                assertionCount = _assertionCount,
                failedCount = _failedCount
            }),
            Summary = $"{stubCount} stub(s), {requestLogCount} request(s) logged, {_assertionCount} assertion(s)"
        };
    }

    private void Emit(string action, string? input, string? output, DateTimeOffset start)
    {
        Interlocked.Increment(ref _assertionCount);
        if (_emitter == null) return;
        var metadata = new Dictionary<string, string>();
        if (input != null)
        {
            var spaceIdx = input.IndexOf(' ');
            if (spaceIdx > 0)
            {
                metadata["http.method"] = input[..spaceIdx];
                metadata["http.url"] = input[(spaceIdx + 1)..];
            }
            else
            {
                metadata["http.url"] = input;
            }
        }
        if (_server != null) metadata["wiremock.stub_count"] = _server.Mappings.Count().ToString();
        _emitter.ReportSuccess(SystemName, action, input: input, output: output, metadata: metadata);
    }

    private bool EmitFailure(string action, string? input, Exception ex, DateTimeOffset start)
    {
        Interlocked.Increment(ref _failedCount);
        if (_emitter != null)
        {
            var metadata = new Dictionary<string, string>();
            if (input != null)
            {
                var spaceIdx = input.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    metadata["http.method"] = input[..spaceIdx];
                    metadata["http.url"] = input[(spaceIdx + 1)..];
                }
            }
            _emitter.ReportFailure(SystemName, action, ex, input: input, metadata: metadata);
        }
        return false;
    }
}
