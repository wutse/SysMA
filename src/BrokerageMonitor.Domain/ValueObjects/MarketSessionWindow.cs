namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class MarketSessionWindow : IEquatable<MarketSessionWindow>
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

    public bool Equals(MarketSessionWindow? other) =>
        other is not null && StartTime == other.StartTime && EndTime == other.EndTime;

    public override bool Equals(object? obj) => Equals(obj as MarketSessionWindow);

    public override int GetHashCode() => HashCode.Combine(StartTime, EndTime);
}
