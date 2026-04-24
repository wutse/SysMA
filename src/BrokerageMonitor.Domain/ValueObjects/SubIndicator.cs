namespace BrokerageMonitor.Domain.ValueObjects;

public enum SubIndicatorStatus
{
    Normal,
    Error
}

public sealed class SubIndicator : IEquatable<SubIndicator>
{
    public string Name { get; }
    public SubIndicatorStatus Status { get; }
    public MetricValue? Metric { get; }

    public SubIndicator(string name, SubIndicatorStatus status, MetricValue? metric = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        Name = name;
        Status = status;
        Metric = metric;
    }

    public bool Equals(SubIndicator? other) =>
        other is not null && Name == other.Name && Status == other.Status && Equals(Metric, other.Metric);

    public override bool Equals(object? obj) => Equals(obj as SubIndicator);

    public override int GetHashCode() => HashCode.Combine(Name, Status, Metric);
}
