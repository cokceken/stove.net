namespace Stove.Net.Core;

/// <summary>
/// Systems implementing this interface are notified when Validate() starts a new trace.
/// Used by TracingSystem to register the traceId→testId mapping for OTLP span correlation.
/// </summary>
public interface ITraceContextAware
{
    /// <summary>Called when a new trace context is created during Validate().</summary>
    void OnTraceStarted(string traceId, string testId);
}
