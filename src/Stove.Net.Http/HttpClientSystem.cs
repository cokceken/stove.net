using System.Net.Http.Json;
using Stove.Net.Core;

namespace Stove.Net.Http;

/// <summary>
/// HTTP client system for making requests and asserting responses.
/// Wraps an HttpClient (typically from WebApplicationFactory).
/// All methods return <see cref="HttpClientSystem"/> for chaining.
/// Use the <c>validate</c> callback to inspect or extract data from responses.
/// </summary>
public class HttpClientSystem(HttpClientSystemOptions options) : IPluggedSystem
{
    private readonly HttpClientSystemOptions _options = options;
    private HttpClient? _httpClient;

    /// <summary>
    /// Sets the underlying HttpClient. Called by the xUnit integration
    /// after WebApplicationFactory creates the client.
    /// </summary>
    public void SetHttpClient(HttpClient client)
    {
        _httpClient = client;
    }

    private HttpClient Client => _httpClient
                                 ?? throw new InvalidOperationException(
                                     "HttpClient is not set. Ensure WithWebApplication<T>() is configured.");

    public Task RunAsync() => Task.CompletedTask;

    public Task CleanupAsync() => Task.CompletedTask;

    // --- GET ---

    /// <summary>
    /// GET and deserialize the response body as <typeparamref name="TResponse"/>.
    /// </summary>
    public async Task<HttpClientSystem> GetAsync<TResponse>(
        string path,
        Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyHeaders(request, headers);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TResponse>()
                   ?? throw new InvalidOperationException(
                       $"Failed to deserialize response body to {typeof(TResponse).Name}");
        validate?.Invoke(body);
        return this;
    }

    /// <summary>
    /// GET with access to the raw <see cref="HttpResponseMessage"/>.
    /// </summary>
    public async Task<HttpClientSystem> GetAsync(
        string path,
        Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyHeaders(request, headers);

        var response = await Client.SendAsync(request);

        if (validate != null)
            validate(response);
        else
            response.EnsureSuccessStatusCode();

        return this;
    }

    // --- POST ---

    /// <summary>
    /// POST and deserialize the response body as <typeparamref name="TResponse"/>.
    /// </summary>
    public async Task<HttpClientSystem> PostAsync<TResponse>(
        string path,
        object? body = null,
        Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                           ?? throw new InvalidOperationException(
                               $"Failed to deserialize response body to {typeof(TResponse).Name}");
        validate?.Invoke(responseBody);
        return this;
    }

    /// <summary>
    /// POST with access to the raw <see cref="HttpResponseMessage"/>.
    /// </summary>
    public async Task<HttpClientSystem> PostAsync(
        string path,
        object? body = null,
        Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);

        if (validate != null)
            validate(response);
        else
            response.EnsureSuccessStatusCode();

        return this;
    }

    // --- PUT ---

    /// <summary>
    /// PUT and deserialize the response body as <typeparamref name="TResponse"/>.
    /// </summary>
    public async Task<HttpClientSystem> PutAsync<TResponse>(
        string path,
        object? body = null,
        Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                           ?? throw new InvalidOperationException(
                               $"Failed to deserialize response body to {typeof(TResponse).Name}");
        validate?.Invoke(responseBody);
        return this;
    }

    /// <summary>
    /// PUT with access to the raw <see cref="HttpResponseMessage"/>.
    /// </summary>
    public async Task<HttpClientSystem> PutAsync(
        string path,
        object? body = null,
        Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);

        if (validate != null)
            validate(response);
        else
            response.EnsureSuccessStatusCode();

        return this;
    }

    // --- DELETE ---

    /// <summary>
    /// DELETE and deserialize the response body as <typeparamref name="TResponse"/>.
    /// </summary>
    public async Task<HttpClientSystem> DeleteAsync<TResponse>(
        string path,
        Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        ApplyHeaders(request, headers);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TResponse>()
                   ?? throw new InvalidOperationException(
                       $"Failed to deserialize response body to {typeof(TResponse).Name}");
        validate?.Invoke(body);
        return this;
    }

    /// <summary>
    /// DELETE with access to the raw <see cref="HttpResponseMessage"/>.
    /// </summary>
    public async Task<HttpClientSystem> DeleteAsync(
        string path,
        Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        ApplyHeaders(request, headers);

        var response = await Client.SendAsync(request);

        if (validate != null)
            validate(response);
        else
            response.EnsureSuccessStatusCode();

        return this;
    }

    // --- PATCH ---

    /// <summary>
    /// PATCH and deserialize the response body as <typeparamref name="TResponse"/>.
    /// </summary>
    public async Task<HttpClientSystem> PatchAsync<TResponse>(
        string path,
        object? body = null,
        Action<TResponse>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<TResponse>()
                           ?? throw new InvalidOperationException(
                               $"Failed to deserialize response body to {typeof(TResponse).Name}");
        validate?.Invoke(responseBody);
        return this;
    }

    /// <summary>
    /// PATCH with access to the raw <see cref="HttpResponseMessage"/>.
    /// </summary>
    public async Task<HttpClientSystem> PatchAsync(
        string path,
        object? body = null,
        Action<HttpResponseMessage>? validate = null,
        Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        ApplyHeaders(request, headers);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var response = await Client.SendAsync(request);

        if (validate != null)
            validate(response);
        else
            response.EnsureSuccessStatusCode();

        return this;
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        if (headers == null) return;
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
    }

    public ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}