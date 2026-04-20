using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Writes immutable audit-log entries for system/component state changes
/// and operator actions. Uses "System" as the operator name for automated events.
/// </summary>
public interface IAuditLogger
{
    Task LogStatusChangedAsync(
        string systemId,
        string componentId,
        ComponentStatus previous,
        ComponentStatus current,
        DateTimeOffset occurredAt,
        CancellationToken ct = default);

    Task LogOperatorActionAsync(
        string systemId,
        string? componentId,
        string actionType,
        string operatorName,
        string? reason,
        DateTimeOffset occurredAt,
        CancellationToken ct = default);
}
