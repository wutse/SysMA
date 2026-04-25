using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when an operator manually overrides a component's state (FR-005/006/007).
/// </summary>
public sealed record ComponentStateOverridden(
    string ComponentId,
    string SystemId,
    ComponentStatus PreviousStatus,
    ComponentStatus NewStatus,
    string OperatorName,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
