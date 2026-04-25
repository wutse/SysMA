namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class MetricValue : IEquatable<MetricValue>
{
    public string Label { get; }
    public decimal Value { get; }

    public MetricValue(string label, decimal value)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label cannot be empty.", nameof(label));

        Label = label;
        Value = value;
    }

    public bool Equals(MetricValue? other) =>
        other is not null && Label == other.Label && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as MetricValue);

    public override int GetHashCode() => HashCode.Combine(Label, Value);
}
