using BrokerageMonitor.Application.Messaging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Processes incoming ZeroMQ heartbeat / status-update messages and updates component state.
/// Messages arrive both periodically (keep-alive) and event-driven (anomaly reports, FR-002).
/// Computes sub-indicator worst-case rollup to determine final status, resets the heartbeat
/// timer, and raises <c>ComponentStatusChanged</c> / <c>ComponentHeartbeatReceived</c> events.
/// </summary>
public interface IHeartbeatProcessor
{
    Task ProcessAsync(HeartbeatMessage message, CancellationToken ct = default);
}
