namespace Stove.Net.Core;

/// <summary>
/// Systems implementing this interface can report their current state as a snapshot.
/// At test end, StoveInstance auto-collects snapshots from all systems implementing
/// this interface and emits them through the event bus — enabling dashboard visualization.
///
/// Inspired by Kotlin Stove's <c>Reports</c> interface.
/// </summary>
public interface IReportsState
{
    /// <summary>
    /// Return a point-in-time snapshot of this system's state.
    /// Called automatically at the end of each test.
    /// </summary>
    StoveSnapshot Report();
}
