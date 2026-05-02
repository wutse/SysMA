using Microsoft.Extensions.Options;

namespace BrokerageMonitor.Web.Services;

/// <summary>
/// Validates <see cref="SchedulerOptions"/> at application startup so that
/// misconfigured cron expressions fail fast rather than throwing at scheduling time.
/// </summary>
public sealed class SchedulerOptionsValidator : IValidateOptions<SchedulerOptions>
{
  /// <inheritdoc />
  public ValidateOptionsResult Validate(string? name, SchedulerOptions options)
  {
    var errors = new List<string>();

    if (!IsValidQuartzCron(options.SmokeTestCron))
      errors.Add($"Scheduler:SmokeTestCron '{options.SmokeTestCron}' is not a valid 6-field Quartz cron expression.");

    if (!IsValidQuartzCron(options.DailyExecutionCreatorCron))
      errors.Add($"Scheduler:DailyExecutionCreatorCron '{options.DailyExecutionCreatorCron}' is not a valid 6-field Quartz cron expression.");

    return errors.Count > 0
        ? ValidateOptionsResult.Fail(errors)
        : ValidateOptionsResult.Success;
  }

  /// <summary>
  /// Performs a lightweight structural check: non-empty, exactly 6 whitespace-delimited fields.
  /// Quartz cron expressions have the form: seconds minutes hours day-of-month month day-of-week.
  /// </summary>
  private static bool IsValidQuartzCron(string? cron) =>
      !string.IsNullOrWhiteSpace(cron) &&
      cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6;
}
