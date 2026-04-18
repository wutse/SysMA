namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a HealthMonitorDefinition's deadline time arrives (Quartz.NET trigger).
/// Subscribed by: HealthEvaluationService.
/// </summary>
public sealed record AggregateHealthDeadlineReached(
    Guid DefinitionId,
    string SystemId,
    DateOnly ExecutionDate,
    DateTimeOffset OccurredAt) : IDomainEvent;
