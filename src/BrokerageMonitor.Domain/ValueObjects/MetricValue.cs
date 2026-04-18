namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class MetricValue
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

    public override bool Equals(object? obj) =>
        obj is MetricValue other && Label == other.Label && Value == other.Value;

    public override int GetHashCode() => HashCode.Combine(Label, Value);
}
