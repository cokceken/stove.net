using Microsoft.Extensions.Logging;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Core.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that captures log output into <see cref="StoveLogCapture"/> for test correlation.
/// </summary>
internal sealed class StoveLoggerProvider : ILoggerProvider
{
    private readonly StoveLogCapture _capture;
    private readonly LogLevel _minimumLevel;

    internal StoveLoggerProvider(StoveLogCapture capture, LogLevel minimumLevel)
    {
        _capture = capture;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new StoveLogger(_capture, categoryName, _minimumLevel);

    public void Dispose() { }
}

internal sealed class StoveLogger : ILogger
{
    private readonly StoveLogCapture _capture;
    private readonly string _category;
    private readonly LogLevel _minimumLevel;

    internal StoveLogger(StoveLogCapture capture, string category, LogLevel minimumLevel)
    {
        _capture = capture;
        _category = category;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var testId = StoveLogCapture.CurrentTestId.Value;
        if (string.IsNullOrEmpty(testId))
            return;

        _capture.Add(new LogLine(
            DateTimeOffset.UtcNow,
            ToAbbreviation(logLevel),
            _category,
            formatter(state, exception)));
    }

    private static string ToAbbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => level.ToString()[..3].ToUpperInvariant()
    };
}
