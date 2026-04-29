using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Xunit;

/// <summary>
/// ASP.NET Core middleware that captures HTTP request and response bodies
/// and emits them as StoveSnapshot events for the dashboard.
/// Opt-in: register via UseStoveBodyCapture() in ConfigureWebHost.
/// </summary>
public sealed class StoveBodyCaptureMiddleware(
    RequestDelegate next,
    StoveBodyCaptureOptions options,
    IStoveEventEmitter emitter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        string? requestBody = null;
        string? responseBody = null;

        // Capture request body
        if (options.CaptureRequest && context.Request.ContentLength > 0)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            if (requestBody.Length > options.MaxBodySize)
                requestBody = requestBody[..options.MaxBodySize] + "…[truncated]";
            context.Request.Body.Position = 0;
        }

        // Wrap response body to capture it
        var originalBodyStream = context.Response.Body;
        using var captureStream = options.CaptureResponse ? new MemoryStream() : null;

        if (captureStream != null)
            context.Response.Body = captureStream;

        try
        {
            await next(context);
        }
        finally
        {
            if (captureStream != null)
            {
                captureStream.Position = 0;
                using var reader = new StreamReader(captureStream, leaveOpen: true);
                responseBody = await reader.ReadToEndAsync();
                if (responseBody.Length > options.MaxBodySize)
                    responseBody = responseBody[..options.MaxBodySize] + "…[truncated]";

                captureStream.Position = 0;
                await captureStream.CopyToAsync(originalBodyStream);
                context.Response.Body = originalBodyStream;
            }
        }

        // Emit snapshot
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? emitter.CurrentTraceId;
        var stateJson = JsonSerializer.Serialize(new
        {
            method = context.Request.Method,
            path = context.Request.Path.Value,
            query = context.Request.QueryString.Value,
            statusCode = context.Response.StatusCode,
            requestContentType = context.Request.ContentType,
            responseContentType = context.Response.ContentType,
            requestBody = options.Redact != null && requestBody != null ? options.Redact(requestBody) : requestBody,
            responseBody = options.Redact != null && responseBody != null ? options.Redact(responseBody) : responseBody
        });

        var summary = $"{context.Request.Method} {context.Request.Path} → {context.Response.StatusCode}";

        emitter.EmitSnapshot(new StoveSnapshot
        {
            TestId = emitter.CurrentTestId,
            TraceId = traceId,
            System = "Http",
            StateJson = stateJson,
            Summary = summary
        });
    }
}

/// <summary>Options for StoveBodyCaptureMiddleware.</summary>
public sealed class StoveBodyCaptureOptions
{
    /// <summary>Whether to capture request bodies. Default: true.</summary>
    public bool CaptureRequest { get; set; } = true;

    /// <summary>Whether to capture response bodies. Default: true.</summary>
    public bool CaptureResponse { get; set; } = true;

    /// <summary>Maximum body size in characters before truncation. Default: 8192.</summary>
    public int MaxBodySize { get; set; } = 8192;

    /// <summary>
    /// Optional redaction function applied to captured bodies.
    /// Use to strip sensitive data (tokens, passwords, PII).
    /// </summary>
    public Func<string, string>? Redact { get; set; }
}

/// <summary>Extension methods to register body capture middleware.</summary>
public static class StoveBodyCaptureExtensions
{
    /// <summary>
    /// Register Stove body capture middleware in the ASP.NET Core pipeline.
    /// Call in ConfigureWebHost:
    /// <code>
    /// protected override void ConfigureWebHost(IWebHostBuilder builder)
    ///     => builder.Configure(app => app.UseStoveBodyCapture(Stove));
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseStoveBodyCapture(
        this IApplicationBuilder app,
        IStoveEventEmitter emitter,
        Action<StoveBodyCaptureOptions>? configure = null)
    {
        var options = new StoveBodyCaptureOptions();
        configure?.Invoke(options);
        app.UseMiddleware<StoveBodyCaptureMiddleware>(options, emitter);
        return app;
    }
}