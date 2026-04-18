using Microsoft.Extensions.Logging;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Lightweight smoke-test job used during startup verification.
/// Executes once and logs a health-check INFO message.
/// Not used in production scheduling — remove or replace in later sprints.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SmokeTestJob : IJob
{
    private readonly ILogger<SmokeTestJob> _logger;

    public SmokeTestJob(ILogger<SmokeTestJob> logger)
    {
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("SmokeTestJob executed successfully at {FiredAt}.", context.FireTimeUtc);
        return Task.CompletedTask;
    }
}
