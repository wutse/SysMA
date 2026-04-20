using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Manages per-component heartbeat timers.
/// Implemented by <c>HeartbeatTimeoutMonitor</c> (Infrastructure).
/// Called by <c>IHeartbeatProcessor</c> to reset the timer on each received heartbeat (US-026).
/// </summary>
public interface IHeartbeatTimerRegistry
{
    /// <summary>
    /// Registers a new per-component timer. Safe to call multiple times for the same
    /// <paramref name="componentId"/>; subsequent calls replace the existing registration.
    /// </summary>
    void RegisterComponent(
        string componentId,
        string systemId,
        ComponentType componentType,
        int timeoutSeconds);

    /// <summary>
    /// Resets the countdown for the specified component's heartbeat timer.
    /// Has no effect if the component is not registered.
    /// </summary>
    void ResetTimer(string componentId);

    /// <summary>
    /// Removes and disposes the timer for the specified component.
    /// Has no effect if the component is not registered.
    /// </summary>
    void UnregisterComponent(string componentId);
}
