using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Http;

/// <summary>
/// HTTP client system for making requests and asserting responses.
/// All methods return HttpClientSystem for chaining.
/// </summary>
public class HttpClientSystem : IPluggedSystem, IStoveReportingSystem, IReportsState
{
    private const string SystemName = "Http";
    private HttpClient? _httpClient;
    private IStoveEventEmitter? _emitter;
    private int _requestCount;
    private int _failedCount;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    /// <summary>Sets the underlying HttpClient.</summary>
    public void SetHttpClient(HttpClient client) => _httpClient = client;

    private HttpClient Client => _httpClient
                                 ?? throw new InvalidOperationException(
                                     "HttpClient is not set. Ensure WithWebApplication<T>() is configured.");

    public Task RunAsync() => Task.CompletedTask;
    public Task CleanupAsync() => Task.CompletedTask;

    // --- GET ---

    public async Task<HttpClientSystem> GetAsync<TResponse>(
        string path, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers, token);
            var response = await Client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<TResponse>()
                       ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(body);
            Emit("GET", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("GET", url, ex, start)) { }
        return this;
    }

    public async Task<HttpClientSystem> GetAsync(
        string path, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers, token);
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("GET", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("GET", url, ex, start)) { }
        return this;
    }

    /// <summary>GET request that deserializes the response as a list of T.</summary>
    public async Task<HttpClientSystem> GetManyAsync<TResponse>(
        string path, Action<List<TResponse>>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers, token);
            var response = await Client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<List<TResponse>>()
                       ?? throw new InvalidOperationException($"Failed to deserialize to List<{typeof(TResponse).Name}>");
            validate?.Invoke(body);
            Emit("GET", url, $"{(int)response.StatusCode} {response.StatusCode} [{body.Count} item(s)]", start);
        }
        catch (Exception ex) when (EmitFailure("GET", url, ex, start)) { }
        return this;
    }

    // --- POST ---

    public async Task<HttpClientSystem> PostAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("POST", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("POST", url, ex, start)) { }
        return this;
    }

    public async Task<HttpClientSystem> PostAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("POST", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("POST", url, ex, start)) { }
        return this;
    }

    /// <summary>POST multipart form data and validate the deserialized response.</summary>
    public async Task<HttpClientSystem> PostMultipartAsync<TResponse>(
        string path, IEnumerable<StoveMultiPartContent> parts, Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null, string? token = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            ApplyHeaders(request, headers, token);
            using var content = new MultipartFormDataContent();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case StoveMultiPartContent.Text text:
                        content.Add(new StringContent(text.Value, Encoding.UTF8), text.Name);
                        break;
                    case StoveMultiPartContent.Binary binary:
                        content.Add(new ByteArrayContent(binary.Data), binary.Name, binary.FileName);
                        break;
                    case StoveMultiPartContent.File file:
                        var stream = new StreamContent(file.Stream);
                        if (file.ContentType != null)
                            stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                        content.Add(stream, file.Name, file.FileName);
                        break;
                }
            }
            request.Content = content;
            var response = await Client.SendAsync(request);
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("POST (multipart)", path, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("POST (multipart)", path, ex, start)) { }
        return this;
    }

    /// <summary>POST multipart form data and validate the raw response.</summary>
    public async Task<HttpClientSystem> PostMultipartAsync(
        string path, IEnumerable<StoveMultiPartContent> parts, Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null, string? token = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            ApplyHeaders(request, headers, token);
            using var content = new MultipartFormDataContent();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case StoveMultiPartContent.Text text:
                        content.Add(new StringContent(text.Value, Encoding.UTF8), text.Name);
                        break;
                    case StoveMultiPartContent.Binary binary:
                        content.Add(new ByteArrayContent(binary.Data), binary.Name, binary.FileName);
                        break;
                    case StoveMultiPartContent.File file:
                        var stream = new StreamContent(file.Stream);
                        if (file.ContentType != null)
                            stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                        content.Add(stream, file.Name, file.FileName);
                        break;
                }
            }
            request.Content = content;
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("POST (multipart)", path, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("POST (multipart)", path, ex, start)) { }
        return this;
    }

    // --- PUT ---

    public async Task<HttpClientSystem> PutAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("PUT", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("PUT", url, ex, start)) { }
        return this;
    }

    public async Task<HttpClientSystem> PutAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("PUT", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("PUT", url, ex, start)) { }
        return this;
    }

    // --- DELETE ---

    public async Task<HttpClientSystem> DeleteAsync<TResponse>(
        string path, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyHeaders(request, headers, token);
            var response = await Client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<TResponse>()
                       ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(body);
            Emit("DELETE", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("DELETE", url, ex, start)) { }
        return this;
    }

    public async Task<HttpClientSystem> DeleteAsync(
        string path, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyHeaders(request, headers, token);
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("DELETE", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("DELETE", url, ex, start)) { }
        return this;
    }

    // --- PATCH ---

    public async Task<HttpClientSystem> PatchAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("PATCH", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("PATCH", url, ex, start)) { }
        return this;
    }

    public async Task<HttpClientSystem> PatchAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            ApplyHeaders(request, headers, token);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            validate?.Invoke(response);
            Emit("PATCH", url, $"{(int)response.StatusCode} {response.StatusCode}", start);
        }
        catch (Exception ex) when (EmitFailure("PATCH", url, ex, start)) { }
        return this;
    }

    // --- STREAMING (NDJSON) ---

    /// <summary>
    /// Reads an NDJSON (newline-delimited JSON) streaming endpoint and yields each
    /// deserialized item as an IAsyncEnumerable.
    /// </summary>
    public async IAsyncEnumerable<TResponse> GetStreamAsync<TResponse>(
        string path, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null, string? token = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(path, queryParams);
        var start = DateTimeOffset.UtcNow;
        var count = 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, headers, token);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null
               && !cancellationToken.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var item = JsonSerializer.Deserialize<TResponse>(line)
                       ?? throw new InvalidOperationException($"Failed to deserialize stream line to {typeof(TResponse).Name}");
            count++;
            yield return item;
        }

        Emit("GET (stream)", url, $"{(int)response.StatusCode} [{count} item(s)]", start);
    }

    // --- Helpers ---

    private static string BuildUrl(string path, Dictionary<string, string>? queryParams)
    {
        if (queryParams is not { Count: > 0 }) return path;

        var sb = new StringBuilder(path);
        sb.Append(path.Contains('?') ? '&' : '?');
        var first = true;
        foreach (var (key, value) in queryParams)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
            first = false;
        }
        return sb.ToString();
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers, string? token = null)
    {
        if (token != null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (headers == null) return;
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
    }

    public StoveSnapshot Report() => new()
    {
        System = SystemName,
        StateJson = JsonSerializer.Serialize(new { requestCount = _requestCount, failedCount = _failedCount }),
        Summary = $"{_requestCount} request(s), {_failedCount} failed"
    };

    private void Emit(string action, string? input, string? output, DateTimeOffset start)
    {
        Interlocked.Increment(ref _requestCount);
        if (_emitter == null) return;
        var traceId = _emitter.CurrentTraceId;
        var spanId = StoveSpan.NewSpanId();
        var metadata = new Dictionary<string, string>
        {
            ["http.method"] = action,
            ["http.url"] = input ?? string.Empty,
            ["scope.type"] = "http_request",
            ["scope.id"] = spanId,
            ["scope.name"] = $"{action} {input}"
        };
        if (output != null)
        {
            var spaceIdx = output.IndexOf(' ');
            if (spaceIdx > 0) metadata["http.status_code"] = output[..spaceIdx];
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
            TraceId = traceId, SpanId = spanId,
            ParentSpanId = _emitter.CurrentSpanId,
            OperationName = action, ServiceName = SystemName,
            Start = start, End = DateTimeOffset.UtcNow, Status = "ok",
            Attributes = new Dictionary<string, string>
            {
                ["scope.type"] = "http_request",
                ["scope.id"] = spanId
            }
        });
    }

    private bool EmitFailure(string action, string? input, Exception ex, DateTimeOffset start)
    {
        Interlocked.Increment(ref _failedCount);
        if (_emitter != null)
        {
            var traceId = _emitter.CurrentTraceId;
            var metadata = new Dictionary<string, string>
            {
                ["http.method"] = action,
                ["http.url"] = input ?? string.Empty
            };
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

    public ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}
