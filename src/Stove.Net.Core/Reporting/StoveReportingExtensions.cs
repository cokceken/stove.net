namespace Stove.Net.Core.Reporting;

/// <summary>
/// Extension methods for simplified plugin reporting.
/// Systems call these after performing their action to emit a StoveEntry
/// with the appropriate success/failure result.
/// </summary>
public static class StoveReportingExtensions
{
    /// <summary>
    /// Report a completed plugin action (when timing is handled externally).
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
    /// Report a failed plugin action (when timing is handled externally).
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