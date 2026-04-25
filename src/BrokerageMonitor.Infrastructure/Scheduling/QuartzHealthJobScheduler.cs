using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Infrastructure.Scheduling;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Quartz-backed implementation of <see cref="IHealthJobScheduler"/>.
/// Schedules or replaces the <see cref="AggregateHealthEvaluationJob"/> trigger
/// for a given definition so that runtime upserts are evaluated immediately
/// without requiring a host restart (US-051 / FR-045).
/// </summary>
public sealed class QuartzHealthJobScheduler : IHealthJobScheduler
{
    private readonly ISchedulerFactory _schedulerFactory;

    public QuartzHealthJobScheduler(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    /// <inheritdoc/>
    public async Task ScheduleOrRescheduleAsync(
        Guid definitionId,
        TimeOnly deadlineTime,
        CancellationToken ct = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(ct).ConfigureAwait(false);
        var cron = $"0 {deadlineTime.Minute} {deadlineTime.Hour} * * ?";
        var jobData = new JobDataMap
        {
            [AggregateHealthEvaluationJob.DefinitionIdKey] = definitionId.ToString()
        };

        var jobName = $"AggregateHealthEval-{definitionId}";
        await QuartzJobScheduler.ScheduleCronJobAsync<AggregateHealthEvaluationJob>(
            scheduler, cron, jobName, jobData, ct).ConfigureAwait(false);
    }
}
