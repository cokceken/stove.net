namespace Stove.Net.Core;

/// <summary>
/// Marker interface for systems that can emit structured operation events.
/// Systems implementing this receive an IStoveEventEmitter automatically
/// when registered via StoveBuilder.
/// </summary>
public interface IStoveReportingSystem
{
    /// <summary>
    /// Called by StoveInstance after the system is registered.
    /// Store the emitter and use it inside assertion/action methods.
    /// </summary>
    void SetEmitter(IStoveEventEmitter emitter);
}
