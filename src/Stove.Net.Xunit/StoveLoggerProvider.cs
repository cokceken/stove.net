using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Stove.Net.Core;

namespace Stove.Net.Xunit;

/// <summary>
/// Options for controlling which application logs Stove captures.
/// </summary>
public sealed class StoveLogCaptureOptions
{
    /// <summary>Minimum log level to capture. Default: Information.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Maximum log message length before truncation. Default: 2048 characters.
    /// </summary>
    public int MaxMessageLength { get; set; } = 2048;

    /// <summary>
    /// Category prefixes to exclude from capture.
    /// Default: Stove.Net and Grpc.Net.Client (avoids recursion from dashboard reporting).
    /// </summary>
    public List<string> ExcludedCategoryPrefixes { get; set; } =
    [
        "Stove.Net",
        "Grpc.Net.Client"
    ];

    /// <summary>
    /// If set, only categories starting with one of these prefixes are captured.
    /// When empty (default), all categories are captured (minus excluded).
    /// </summary>
    public List<string> IncludedCategoryPrefixes { get; set; } = [];
}

/// <summary>
/// ILoggerProvider that captures the SUT's log output and emits it as StoveEntry events.
/// This allows application logs (controller actions, service operations, database calls)
/// to appear in the Stove dashboard alongside test assertions and spans.
///
/// Injected automatically by StoveFixture into the WebApplicationFactory's logging pipeline.
/// </summary>
public sealed class StoveLoggerProvider : ILoggerProvider
{
    private readonly IStoveEventEmitter _emitter;
    private readonly StoveLogCaptureOptions _options;

    public StoveLoggerProvider(IStoveEventEmitter emitter, StoveLogCaptureOptions? options = null)
    {
        _emitter = emitter;
        _options = options ?? new StoveLogCaptureOptions();
    }

    public ILogger CreateLogger(string categoryName)
        => new StoveLogger(categoryName, _emitter, _options);

    public void Dispose() { }
}

/// <summary>
/// Logger that forwards log messages to the Stove event bus as EntryRecordedEvent entries.
/// Automatically correlates logs with the current test ID and trace ID via Activity.Current.
/// </summary>
internal sealed class StoveLogger(
    string categoryName,
    IStoveEventEmitter emitter,
    StoveLogCaptureOptions options) : ILogger
{
    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel < options.MinimumLevel || logLevel == LogLevel.None)
            return false;

        // Prevent recursion: exclude Stove's own logs and gRPC client logs
        foreach (var prefix in options.ExcludedCategoryPrefixes)
        {
            if (categoryName.StartsWith(prefix, StringComparison.Ordinal))
                return false;
        }

        // If include filter is set, only capture matching categories
        if (options.IncludedCategoryPrefixes is { Count: > 0 })
        {
            var matched = false;
            foreach (var prefix in options.IncludedCategoryPrefixes)
            {
                if (categoryName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched) return false;
        }

        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null) return;

        // Truncate long messages
        if (message.Length > options.MaxMessageLength)
            message = message[..options.MaxMessageLength] + "…[truncated]";

        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString() ?? emitter.CurrentTraceId;

        // Shorten category for display: "MyApp.Services.OrderService" → "OrderService"
        var shortCategory = categoryName;
        var lastDot = categoryName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < categoryName.Length - 1)
            shortCategory = categoryName[(lastDot + 1)..];

        var metadata = new Dictionary<string, string>
        {
            ["log.level"] = logLevel.ToString(),
            ["log.category"] = categoryName
        };

        if (eventId.Id != 0)
            metadata["log.event_id"] = eventId.Id.ToString();
        if (eventId.Name != null)
            metadata["log.event_name"] = eventId.Name;

        var isError = logLevel >= LogLevel.Error;

        emitter.Emit(new StoveEntry
        {
            TestId = emitter.CurrentTestId,
            TraceId = traceId,
            System = "Application",
            Action = $"{logLevel}",
            Result = isError ? EntryResult.Failed : EntryResult.Success,
            Input = shortCategory,
            Output = message,
            Error = exception?.ToString(),
            Metadata = metadata
        });
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => null;
}
