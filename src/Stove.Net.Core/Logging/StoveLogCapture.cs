using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Stove.Net.Core.Reporting;

namespace Stove.Net.Core.Logging;

/// <summary>
/// Thread-safe log buffer that captures ILogger output per test.
/// Register via <see cref="CreateProvider"/> in WebApplicationFactory's ConfigureLogging.
/// </summary>
public sealed class StoveLogCapture
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<LogLine>> _logs = new();

    /// <summary>
    /// AsyncLocal for test ID correlation — set by StoveInstance before each test.
    /// </summary>
    internal static readonly AsyncLocal<string?> CurrentTestId = new();

    /// <summary>Set the current test ID for log correlation.</summary>
    public void SetTestId(string testId) => CurrentTestId.Value = testId;

    /// <summary>Clear the current test ID.</summary>
    public void ClearTestId() => CurrentTestId.Value = null;

    /// <summary>Get and clear all logs for a test, sorted by timestamp.</summary>
    public IReadOnlyList<LogLine> GetLogs(string testId)
    {
        if (!_logs.TryRemove(testId, out var bag))
            return [];

        return bag.OrderBy(l => l.Timestamp).ToList();
    }

    /// <summary>Create an <see cref="ILoggerProvider"/> that writes to this capture.</summary>
    public ILoggerProvider CreateProvider(LogLevel minimumLevel = LogLevel.Information)
        => new StoveLoggerProvider(this, minimumLevel);

    internal void Add(LogLine line)
    {
        var testId = CurrentTestId.Value;
        if (string.IsNullOrEmpty(testId))
            return;

        _logs.GetOrAdd(testId, _ => new ConcurrentBag<LogLine>()).Add(line);
    }
}
