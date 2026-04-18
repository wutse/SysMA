namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a component heartbeat timer expires (FR-003, BI-011).
/// ScheduledJob: Lost only when in Running state.
/// Service: Lost applies in any state except Stopped.
/// </summary>
public sealed record ComponentLost(
    string ComponentId,
    string SystemId,
    DateTimeOffset OccurredAt) : IDomainEvent;
