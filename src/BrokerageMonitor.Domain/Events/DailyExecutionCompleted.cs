using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a DailyExecution reaches Success or Failed terminal status.
/// </summary>
public sealed record DailyExecutionCompleted(
    Guid ExecutionId,
    Guid DefinitionId,
    string SystemId,
    DailyExecutionStatus FinalStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
