using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Entity representing the current runtime state of a monitored component.
/// Hot data — maintained in-memory and persisted via SQLite upsert (FR-033).
/// </summary>
public sealed class ComponentState
{
    private readonly List<SubIndicator> _subIndicators = [];

    public string ComponentId { get; private init; }
    public ComponentStatus Status { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public DateTimeOffset LastStatusChangedAt { get; private set; }
    public IReadOnlyList<SubIndicator> SubIndicators => _subIndicators.AsReadOnly();

    // Required for Dapper materialization
    private ComponentState()
    {
        ComponentId = null!;
    }

    public ComponentState(string componentId, ComponentStatus initialStatus = ComponentStatus.Unknown)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        ComponentId = componentId;
        Status = initialStatus;
        LastStatusChangedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateStatus(ComponentStatus newStatus, DateTimeOffset changedAt)
    {
        Status = newStatus;
        LastStatusChangedAt = changedAt;
    }

    public void RecordHeartbeat(DateTimeOffset receivedAt) => LastHeartbeatAt = receivedAt;

    public void SetSubIndicators(IEnumerable<SubIndicator> subIndicators)
    {
        ArgumentNullException.ThrowIfNull(subIndicators);
        _subIndicators.Clear();
        _subIndicators.AddRange(subIndicators);
    }
}
