using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Projection used by HealthManagementPage and HealthDefinitionEditorPage.
/// US-046, US-047, EP-009.
/// </summary>
public sealed record HealthMonitorDefinitionDto(
    Guid DefinitionId,
    string SystemId,
    string Name,
    TimeOnly DeadlineTime,
    ScheduleType ScheduleType,
    string? CronExpression,
    DayOfWeek? ScheduleDayOfWeek,
    IReadOnlyList<WatchedComponentDto> WatchedComponents,
    IReadOnlyList<string> EmailRecipients,
    string? TeamsWebhookUrl,
    bool SendOnFailure,
    bool IsActive,
    DailyExecutionDto? TodayExecution);

/// <summary>
/// Lightweight projection of a watched component within a definition.
/// </summary>
public sealed record WatchedComponentDto(
    string ComponentId,
    string ComponentName,
    ComponentType ComponentType);
