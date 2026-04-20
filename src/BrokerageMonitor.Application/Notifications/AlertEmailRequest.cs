using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Request payload for sending an alert email notification (FR-010, FR-037).
/// Recipients correspond to <c>MonitoredSystem.AlertRecipients</c>.
/// </summary>
public sealed record AlertEmailRequest(
    Guid AlertId,
    string SystemId,
    string SystemName,
    string ComponentId,
    string ComponentName,
    ComponentStatus AlertStatus,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string> Recipients);
