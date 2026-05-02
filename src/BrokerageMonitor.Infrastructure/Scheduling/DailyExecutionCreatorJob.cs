using BrokerageMonitor.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Quartz job that creates daily execution instances at 05:30 every morning.
/// Delegates to <see cref="IDailyExecutionCreatorService.CreateForDateAsync"/> for today's date.
/// US-048 (EP-009). FR-042, BI-012, BI-015.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DailyExecutionCreatorJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DailyExecutionCreatorJob> _logger;

    public DailyExecutionCreatorJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<DailyExecutionCreatorJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        _logger.LogInformation("DailyExecutionCreatorJob started for date {Date}.", today);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IDailyExecutionCreatorService>();
            await service.CreateForDateAsync(today, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "DailyExecutionCreatorJob completed for date {Date}.", today);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DailyExecutionCreatorJob failed for date {Date}.", today);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
