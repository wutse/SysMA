namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class MarketSessionWindow
{
    public TimeOnly StartTime { get; }
    public TimeOnly EndTime { get; }

    public MarketSessionWindow(TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime >= endTime)
            throw new ArgumentException("StartTime must be before EndTime.", nameof(startTime));

        StartTime = startTime;
        EndTime = endTime;
    }

    public bool IsWithinSession(TimeOnly time) =>
        time >= StartTime && time <= EndTime;

    public bool IsWithinSession(DateTimeOffset dateTime) =>
        IsWithinSession(TimeOnly.FromTimeSpan(dateTime.TimeOfDay));

    public override bool Equals(object? obj) =>
        obj is MarketSessionWindow other &&
        StartTime == other.StartTime &&
        EndTime == other.EndTime;

    public override int GetHashCode() => HashCode.Combine(StartTime, EndTime);
}
