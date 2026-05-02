namespace BrokerageMonitor.Web.Services;

/// <summary>
/// Cron schedule configuration for static Quartz jobs.
/// Bind from <c>appsettings.json</c> section <c>"Scheduler"</c>.
/// </summary>
public sealed class SchedulerOptions
{
  public const string SectionName = "Scheduler";

  /// <summary>Cron for <see cref="BrokerageMonitor.Infrastructure.Scheduling.SmokeTestJob"/>. Default: daily at 01:00.</summary>
  public string SmokeTestCron { get; init; } = "0 0 1 * * ?";

  /// <summary>Cron for <see cref="BrokerageMonitor.Infrastructure.Scheduling.DailyExecutionCreatorJob"/>. Default: daily at 05:30.</summary>
  public string DailyExecutionCreatorCron { get; init; } = "0 30 5 * * ?";
}
