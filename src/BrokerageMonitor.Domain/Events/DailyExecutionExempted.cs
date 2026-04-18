namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a DailyExecution is exempted because the system is in maintenance mode
/// at deadline time (FR-016, BI-006).
/// </summary>
public sealed record DailyExecutionExempted(
    Guid ExecutionId,
    Guid DefinitionId,
    string SystemId,
    DateTimeOffset OccurredAt) : IDomainEvent;
