namespace BrokerageMonitor.Domain.ValueObjects;

public enum ScheduleType
{
    Daily,
    Weekly,
    Cron
}

public sealed class HealthRuleSchedule : IEquatable<HealthRuleSchedule>
{
    public ScheduleType ScheduleType { get; }
    public string? CronExpression { get; }
    public DayOfWeek? DayOfWeek { get; }

    public HealthRuleSchedule(ScheduleType scheduleType, string? cronExpression = null, DayOfWeek? dayOfWeek = null)
    {
        if (scheduleType == ScheduleType.Cron && string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required for Cron schedule type.", nameof(cronExpression));

        if (scheduleType == ScheduleType.Weekly && dayOfWeek is null)
            throw new ArgumentException("DayOfWeek is required for Weekly schedule type.", nameof(dayOfWeek));

        ScheduleType = scheduleType;
        CronExpression = cronExpression;
        DayOfWeek = dayOfWeek;
    }

    public bool IsMatch(DateOnly date) => ScheduleType switch
    {
        ScheduleType.Daily => true,
        ScheduleType.Weekly => DayOfWeek.HasValue && date.DayOfWeek == DayOfWeek.Value,
        ScheduleType.Cron => MatchesCron(date),
        _ => false
    };

    private bool MatchesCron(DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(CronExpression))
            return false;

        // Standard 5-field cron: minute hour dayOfMonth month dayOfWeek
        // minute (parts[0]) and hour (parts[1]) are intentionally ignored — this
        // method matches a date, not a date-time; time-of-day is handled by the scheduler.
        var parts = CronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return false;

        // parts[2] = day of month, parts[3] = month, parts[4] = day of week
        return MatchesCronField(parts[2], date.Day, 1, 31) &&
               MatchesCronField(parts[3], date.Month, 1, 12) &&
               MatchesCronField(parts[4], (int)date.DayOfWeek, 0, 6);
    }

    private static bool MatchesCronField(string field, int value, int min, int max)
    {
        if (field == "*") return true;

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2 ||
                    !int.TryParse(stepParts[1], out var step) ||
                    step <= 0)
                    continue;

                var rangeStart = stepParts[0] == "*"
                    ? min
                    : int.TryParse(stepParts[0], out var parsedStart) ? parsedStart : min;

                for (var i = rangeStart; i <= max; i += step)
                {
                    if (i == value) return true;
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out var start) &&
                    int.TryParse(rangeParts[1], out var end) &&
                    value >= start && value <= end)
                {
                    return true;
                }
            }
            else if (int.TryParse(part, out var literal) && literal == value)
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(HealthRuleSchedule? other) =>
        other is not null &&
        ScheduleType == other.ScheduleType &&
        CronExpression == other.CronExpression &&
        DayOfWeek == other.DayOfWeek;

    public override bool Equals(object? obj) => Equals(obj as HealthRuleSchedule);

    public override int GetHashCode() => HashCode.Combine(ScheduleType, CronExpression, DayOfWeek);
}
