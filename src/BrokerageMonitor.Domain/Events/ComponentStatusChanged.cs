using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a component's rolled-up status changes.
/// Subscribed by: AlertEvaluationService, AggregateHealthContext (FR-034), SignalR Hub, AuditLogger.
/// </summary>
public sealed record ComponentStatusChanged(
    string ComponentId,
    string SystemId,
    ComponentStatus PreviousStatus,
    ComponentStatus NewStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
