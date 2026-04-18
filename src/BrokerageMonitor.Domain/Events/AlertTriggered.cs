using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when an alert is triggered for a component during market session (FR-010).
/// </summary>
public sealed record AlertTriggered(
    Guid AlertId,
    string SystemId,
    string ComponentId,
    ComponentStatus TriggerStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
