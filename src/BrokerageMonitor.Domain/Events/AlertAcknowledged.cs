namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when an alert is acknowledged by an operator (FR-018, FR-019).
/// </summary>
public sealed record AlertAcknowledged(
    Guid AlertId,
    string SystemId,
    string OperatorName,
    DateTimeOffset OccurredAt) : IDomainEvent;
