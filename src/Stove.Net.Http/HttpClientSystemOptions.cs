namespace Stove.Net.Http;

/// <summary>
/// Configuration options for the HTTP client system.
/// </summary>
public class HttpClientSystemOptions
{
    /// <summary>
    /// Base URL for HTTP requests. When using WebApplicationFactory, this is set automatically.
    /// </summary>
    public string? BaseUrl { get; set; }
}
