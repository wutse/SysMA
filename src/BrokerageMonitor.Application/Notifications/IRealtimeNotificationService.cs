using BrokerageMonitor.Domain.Events;

namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Abstraction over SignalR push notifications.
/// US-036 (EP-007) provides the <c>SignalRNotificationService</c> implementation.
/// FR-010, FR-030.
/// </summary>
public interface IRealtimeNotificationService
{
    Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default);
    Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default);
    Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default);
    Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default);
    Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default);
}
