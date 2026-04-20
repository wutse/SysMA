using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Request payload for sending a health-summary email after aggregate health evaluation
/// (FR-013, FR-014).
/// </summary>
public sealed record HealthSummaryEmailRequest(
    Guid DefinitionId,
    Guid ExecutionId,
    string DefinitionName,
    string SystemId,
    string SystemName,
    DailyExecutionStatus ExecutionStatus,
    DateOnly ExecutionDate,
    IReadOnlyList<string> FailedComponents,
    IReadOnlyList<string> Recipients);
