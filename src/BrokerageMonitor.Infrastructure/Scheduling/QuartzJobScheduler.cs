using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Centralises Quartz scheduler registration and job scheduling.
/// Follows SRP: only responsible for wiring the scheduler into DI and
/// providing a typed API for scheduling <see cref="IJob"/> instances.
/// </summary>
public static class QuartzJobScheduler
{
    /// <summary>
    /// Registers the Quartz in-memory scheduler with DI. Call this from the
    /// application's composition root (Program.cs / ServiceCollectionExtensions).
    /// </summary>
    public static IServiceCollection AddQuartzScheduler(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            // In-memory job store — no clustering required for single-node deployment
            q.UseInMemoryStore();
        });

        // QuartzHostedService starts/stops the scheduler with the host lifetime
        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }

    /// <summary>
    /// Schedules a <typeparamref name="TJob"/> using the provided cron expression.
    /// The job key is derived from the job type name to guarantee uniqueness.
    /// </summary>
    /// <typeparam name="TJob">The Quartz <see cref="IJob"/> implementation to schedule.</typeparam>
    /// <param name="scheduler">The Quartz <see cref="IScheduler"/> instance.</param>
    /// <param name="cronExpression">A valid Quartz cron expression.</param>
    /// <param name="jobDataMap">Optional job data passed to the job at execution time.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    public static async Task ScheduleCronJobAsync<TJob>(
        IScheduler scheduler,
        string cronExpression,
        JobDataMap? jobDataMap = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob
    {
        var jobKey = new JobKey(typeof(TJob).Name);

        var jobBuilder = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .StoreDurably();

        if (jobDataMap is not null)
        {
            jobBuilder = jobBuilder.UsingJobData(jobDataMap);
        }

        var job = jobBuilder.Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{typeof(TJob).Name}-trigger")
            .ForJob(jobKey)
            .WithCronSchedule(cronExpression)
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken);
    }
}
