using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.Startup;
using BrokerageMonitor.Infrastructure.Persistence;
using BrokerageMonitor.Infrastructure.Scheduling;
using Quartz;

namespace BrokerageMonitor.Web.Services;

/// <summary>
/// Encapsulates post-build startup orchestration: schema bootstrap, data seeding,
/// Quartz job scheduling, and station recovery.
/// Extracted from Program.cs to honour the Single Responsibility Principle.
/// </summary>
public sealed class WebApplicationStartup
{
    private readonly WebApplication _app;

    public WebApplicationStartup(WebApplication app) => _app = app;

    /// <summary>
    /// Runs all startup steps in the correct order.
    /// </summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        await ScheduleStaticJobsAsync(ct).ConfigureAwait(false);
        await InitialiseDatabaseAsync(ct).ConfigureAwait(false);
        await SeedInitialDataAsync(ct).ConfigureAwait(false);
        await ScheduleHealthEvaluationJobsAsync(ct).ConfigureAwait(false);
        await RunStartupRecoveryAsync(ct).ConfigureAwait(false);
    }

    private async Task ScheduleStaticJobsAsync(CancellationToken ct)
    {
        var scheduler = await _app.Services
            .GetRequiredService<ISchedulerFactory>()
            .GetScheduler(ct)
            .ConfigureAwait(false);

        await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
            scheduler, "0 0 1 * * ?", cancellationToken: ct).ConfigureAwait(false);

        await QuartzJobScheduler.ScheduleCronJobAsync<DailyExecutionCreatorJob>(
            scheduler, "0 30 5 * * ?", cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task InitialiseDatabaseAsync(CancellationToken ct)
    {
        await _app.Services
            .GetRequiredService<DatabaseInitializer>()
            .InitialiseAsync()
            .ConfigureAwait(false);
    }

    private async Task SeedInitialDataAsync(CancellationToken ct)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<AppSettingsImporter>()
            .ImportIfEmptyAsync()
            .ConfigureAwait(false);
    }

    private async Task ScheduleHealthEvaluationJobsAsync(CancellationToken ct)
    {
        var scheduler = await _app.Services
            .GetRequiredService<ISchedulerFactory>()
            .GetScheduler(ct)
            .ConfigureAwait(false);

        await using var scope = _app.Services.CreateAsyncScope();
        var jobScheduler = scope.ServiceProvider.GetRequiredService<IHealthJobScheduler>();
        var definitionRepo = scope.ServiceProvider
            .GetRequiredService<BrokerageMonitor.Domain.Repositories.IHealthMonitorDefinitionRepository>();

        var definitions = await definitionRepo.GetAllActiveAsync(ct).ConfigureAwait(false);
        foreach (var def in definitions)
        {
            await jobScheduler
                .ScheduleOrRescheduleAsync(def.DefinitionId, def.DeadlineTime, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task RunStartupRecoveryAsync(CancellationToken ct)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<IStartupRecoveryService>()
            .RecoverAsync()
            .ConfigureAwait(false);
    }
}
