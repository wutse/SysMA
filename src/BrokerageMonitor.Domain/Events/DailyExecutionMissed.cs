namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a DailyExecution is created as Missed during station restart recovery (FR-044).
/// </summary>
public sealed record DailyExecutionMissed(
    Guid ExecutionId,
    Guid DefinitionId,
    string SystemId,
    DateOnly ExecutionDate,
    string MissedReason,
    DateTimeOffset OccurredAt) : IDomainEvent;
