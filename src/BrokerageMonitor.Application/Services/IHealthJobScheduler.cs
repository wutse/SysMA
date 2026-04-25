namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Schedules (or reschedules) the Quartz job that evaluates a
/// <see cref="Domain.Aggregates.HealthMonitorDefinition"/> at its deadline time.
/// US-051 / FR-045.
/// </summary>
public interface IHealthJobScheduler
{
    /// <summary>
    /// Creates or replaces the Quartz trigger for the specified definition.
    /// Safe to call for both new and existing definitions.
    /// </summary>
    Task ScheduleOrRescheduleAsync(
        Guid definitionId,
        TimeOnly deadlineTime,
        CancellationToken ct = default);
}
