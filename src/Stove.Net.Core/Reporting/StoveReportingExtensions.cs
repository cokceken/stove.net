namespace Stove.Net.Core.Reporting;

/// <summary>
/// Extension methods for simplified plugin reporting.
/// Mirrors Kotlin Stove's <c>Reports.report()</c> pattern — wraps an action,
/// auto-handles timing, success/failure detection, and StoveEntry construction.
/// </summary>
public static class StoveReportingExtensions
{
    /// <summary>
    /// Report a plugin action with automatic timing and success/failure.
    /// The action's return value becomes the entry's Output.
    /// </summary>
    public static async Task<string?> Report(
        this IStoveEventEmitter emitter,
        string system,
        string action,
        string? input = null,
        Dictionary<string, string>? metadata = null,
        Func<Task<string?>>? execute = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var output = execute != null ? await execute() : null;
            emitter.Emit(new StoveEntry
            {
                TestId = emitter.CurrentTestId,
                TraceId = emitter.CurrentTraceId,
                System = system,
                Action = action,
                Result = EntryResult.Success,
                Input = input,
                Output = output,
                Metadata = metadata
            });
            return output;
        }
        catch (Exception ex)
        {
            emitter.Emit(new StoveEntry
            {
                TestId = emitter.CurrentTestId,
                TraceId = emitter.CurrentTraceId,
                System = system,
                Action = action,
                Result = EntryResult.Failed,
                Input = input,
                Error = ex.Message,
                Metadata = metadata
            });
            throw;
        }
    }

    /// <summary>
    /// Report a plugin action that doesn't return output.
    /// </summary>
    public static async Task ReportVoid(
        this IStoveEventEmitter emitter,
        string system,
        string action,
        string? input = null,
        Dictionary<string, string>? metadata = null,
        Func<Task>? execute = null)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            if (execute != null) await execute();
            emitter.Emit(new StoveEntry
            {
                TestId = emitter.CurrentTestId,
                TraceId = emitter.CurrentTraceId,
                System = system,
                Action = action,
                Result = EntryResult.Success,
                Input = input,
                Metadata = metadata
            });
        }
        catch (Exception ex)
        {
            emitter.Emit(new StoveEntry
            {
                TestId = emitter.CurrentTestId,
                TraceId = emitter.CurrentTraceId,
                System = system,
                Action = action,
                Result = EntryResult.Failed,
                Input = input,
                Error = ex.Message,
                Metadata = metadata
            });
            throw;
        }
    }

    /// <summary>
    /// Report a completed plugin action synchronously (when timing is handled externally).
    /// </summary>
    public static void ReportSuccess(
        this IStoveEventEmitter emitter,
        string system,
        string action,
        string? input = null,
        string? output = null,
        Dictionary<string, string>? metadata = null)
    {
        emitter.Emit(new StoveEntry
        {
            TestId = emitter.CurrentTestId,
            TraceId = emitter.CurrentTraceId,
            System = system,
            Action = action,
            Result = EntryResult.Success,
            Input = input,
            Output = output,
            Metadata = metadata
        });
    }

    /// <summary>
    /// Report a failed plugin action synchronously (when timing is handled externally).
    /// </summary>
    public static void ReportFailure(
        this IStoveEventEmitter emitter,
        string system,
        string action,
        Exception ex,
        string? input = null,
        Dictionary<string, string>? metadata = null)
    {
        emitter.Emit(new StoveEntry
        {
            TestId = emitter.CurrentTestId,
            TraceId = emitter.CurrentTraceId,
            System = system,
            Action = action,
            Result = EntryResult.Failed,
            Input = input,
            Error = ex.Message,
            Metadata = metadata
        });
    }
}
