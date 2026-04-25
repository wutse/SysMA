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

    /// <summary>
    /// Private constructor for the <see cref="Rehydrate"/> factory method.
    /// Does not call <see cref="DateTimeOffset.UtcNow"/> — all timestamps come from persistence.
    /// </summary>
    private ComponentState(
        string componentId,
        ComponentStatus status,
        DateTimeOffset lastStatusChangedAt,
        DateTimeOffset? lastHeartbeatAt,
        IEnumerable<SubIndicator>? subIndicators)
    {
        ComponentId = componentId;
        Status = status;
        LastStatusChangedAt = lastStatusChangedAt;
        LastHeartbeatAt = lastHeartbeatAt;
        if (subIndicators is not null)
            _subIndicators.AddRange(subIndicators);
    }

    public ComponentState(string componentId, ComponentStatus initialStatus = ComponentStatus.Unknown)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        ComponentId = componentId;
        Status = initialStatus;
        LastStatusChangedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reconstitutes a <see cref="ComponentState"/> from persisted data without calling
    /// <see cref="DateTimeOffset.UtcNow"/> — making repositories deterministic and testable.
    /// </summary>
    public static ComponentState Rehydrate(
        string componentId,
        ComponentStatus status,
        DateTimeOffset lastStatusChangedAt,
        DateTimeOffset? lastHeartbeatAt,
        IEnumerable<SubIndicator>? subIndicators = null)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        return new ComponentState(componentId, status, lastStatusChangedAt, lastHeartbeatAt, subIndicators);
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
