namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a DailyExecution instance is created (batch create or recovery, FR-042/044).
/// </summary>
public sealed record DailyExecutionCreated(
    Guid ExecutionId,
    Guid DefinitionId,
    string SystemId,
    DateOnly ExecutionDate,
    DateTimeOffset OccurredAt) : IDomainEvent;
