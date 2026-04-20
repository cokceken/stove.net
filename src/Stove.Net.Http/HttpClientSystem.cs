using System.Net.Http.Json;
using Stove.Net.Core;

namespace Stove.Net.Http;

/// <summary>
/// HTTP client system for making requests and asserting responses.
/// All methods return HttpClientSystem for chaining.
/// </summary>
public class HttpClientSystem : IPluggedSystem, IStoveReportingSystem
{
    private const string SystemName = "Http";
    private HttpClient? _httpClient;
    private IStoveEventEmitter? _emitter;

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
        string path, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            ApplyHeaders(request, headers);
            var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TResponse>()
                       ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(body);
            Emit("GET", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("GET", path, ex)) { }
        return this;
    }

    public async Task<HttpClientSystem> GetAsync(
        string path, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            ApplyHeaders(request, headers);
            var response = await Client.SendAsync(request);
            if (validate != null) validate(response); else response.EnsureSuccessStatusCode();
            Emit("GET", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("GET", path, ex)) { }
        return this;
    }

    // --- POST ---

    public async Task<HttpClientSystem> PostAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("POST", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("POST", path, ex)) { }
        return this;
    }

    public async Task<HttpClientSystem> PostAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            if (validate != null) validate(response); else response.EnsureSuccessStatusCode();
            Emit("POST", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("POST", path, ex)) { }
        return this;
    }

    // --- PUT ---

    public async Task<HttpClientSystem> PutAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("PUT", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("PUT", path, ex)) { }
        return this;
    }

    public async Task<HttpClientSystem> PutAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            if (validate != null) validate(response); else response.EnsureSuccessStatusCode();
            Emit("PUT", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("PUT", path, ex)) { }
        return this;
    }

    // --- DELETE ---

    public async Task<HttpClientSystem> DeleteAsync<TResponse>(
        string path, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, path);
            ApplyHeaders(request, headers);
            var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TResponse>()
                       ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(body);
            Emit("DELETE", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("DELETE", path, ex)) { }
        return this;
    }

    public async Task<HttpClientSystem> DeleteAsync(
        string path, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, path);
            ApplyHeaders(request, headers);
            var response = await Client.SendAsync(request);
            if (validate != null) validate(response); else response.EnsureSuccessStatusCode();
            Emit("DELETE", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("DELETE", path, ex)) { }
        return this;
    }

    // --- PATCH ---

    public async Task<HttpClientSystem> PatchAsync<TResponse>(
        string path, object? body = null, Action<TResponse>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                               ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(TResponse).Name}");
            validate?.Invoke(responseBody);
            Emit("PATCH", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("PATCH", path, ex)) { }
        return this;
    }

    public async Task<HttpClientSystem> PatchAsync(
        string path, object? body = null, Action<HttpResponseMessage>? validate = null, Dictionary<string, string>? headers = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, path);
            ApplyHeaders(request, headers);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            if (validate != null) validate(response); else response.EnsureSuccessStatusCode();
            Emit("PATCH", path, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex) when (EmitFailure("PATCH", path, ex)) { }
        return this;
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        if (headers == null) return;
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
    }

    private void Emit(string action, string? input, string? output)
        => _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Success, Input = input, Output = output
        });

    private bool EmitFailure(string action, string? input, Exception ex)
    {
        _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Failed, Input = input, Error = ex.Message
        });
        return false;
    }

    public ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}
