using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// In-process event bus that allows Blazor Server components to subscribe to
/// domain-level monitoring events without going through the SignalR transport.
///
/// Registered as Singleton. Subscriptions are per-Blazor-circuit and must be
/// disposed via <see cref="Unsubscribe"/> when the component is torn down.
/// </summary>
public interface IMonitorBroadcaster
{
    /// <summary>
    /// Fired when any component's rolled-up status changes.
    /// Args: (componentId, systemId, newStatus)
    /// </summary>
    event Action<string, string, ComponentStatus>? ComponentStatusUpdated;

    /// <summary>Fired when an alert is triggered for a system.</summary>
    event Action<string, string>? AlertTriggered;

    /// <summary>Fired when all alerts for a system are acknowledged.</summary>
    event Action<string>? AlertAcknowledged;

    /// <summary>Fired when maintenance mode is toggled for a system.</summary>
    event Action<string, bool>? MaintenanceModeChanged;

    void PublishComponentStatusChanged(string componentId, string systemId, ComponentStatus newStatus);
    void PublishAlertTriggered(string systemId, string componentId);
    void PublishAlertAcknowledged(string systemId);
    void PublishMaintenanceModeChanged(string systemId, bool isActive);
}
