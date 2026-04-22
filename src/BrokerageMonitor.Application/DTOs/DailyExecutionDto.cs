using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Projection used by HealthManagementPage to display daily execution status.
/// US-046, EP-009.
/// </summary>
public sealed record DailyExecutionDto(
    Guid ExecutionId,
    Guid DefinitionId,
    string SystemId,
    DateOnly ExecutionDate,
    DailyExecutionStatus Status,
    IReadOnlyList<string> CompletedComponents,
    IReadOnlyList<string> FailedComponents,
    string? MissedReason,
    DateTimeOffset? EvaluatedAt,
    DateTimeOffset? NotificationSentAt);
