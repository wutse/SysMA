namespace BrokerageMonitor.Domain.ValueObjects;

public sealed class WatchedComponent : IEquatable<WatchedComponent>
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

    public bool Equals(WatchedComponent? other) =>
        other is not null && ComponentId == other.ComponentId && ComponentType == other.ComponentType;

    public override bool Equals(object? obj) => Equals(obj as WatchedComponent);

    public override int GetHashCode() => HashCode.Combine(ComponentId, ComponentType);
}
