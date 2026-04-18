namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class WatchedComponent
{
    public string ComponentId { get; }
    public ComponentType ComponentType { get; }

    public WatchedComponent(string componentId, ComponentType componentType)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        ComponentId = componentId;
        ComponentType = componentType;
    }

    public override bool Equals(object? obj) =>
        obj is WatchedComponent other &&
        ComponentId == other.ComponentId &&
        ComponentType == other.ComponentType;

    public override int GetHashCode() => HashCode.Combine(ComponentId, ComponentType);
}
