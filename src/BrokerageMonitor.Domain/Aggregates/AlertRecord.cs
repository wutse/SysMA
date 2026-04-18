using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing an alert triggered for a monitored component.
/// FR-010, FR-011, FR-017, FR-018, FR-019
/// </summary>
public sealed class AlertRecord
{
    public Guid AlertId { get; private init; }
    public string SystemId { get; private init; }
    public string ComponentId { get; private init; }
    public ComponentStatus AlertStatus { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }

    /// <summary>
    /// Global flag indicating an unacknowledged alert is active for this system (FR-011).
    /// BI-007: prevents duplicate pop-ups while flag is active.
    /// </summary>
    public bool IsGlobalFlagActive { get; private set; }

    public string? AcknowledgedBy { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    // Required for Dapper materialization
    private AlertRecord()
    {
        SystemId = null!;
        ComponentId = null!;
    }

    public AlertRecord(
        Guid alertId,
        string systemId,
        string componentId,
        ComponentStatus alertStatus,
        DateTimeOffset occurredAt,
        bool isGlobalFlagActive = true)
    {
        if (alertId == Guid.Empty)
            throw new ArgumentException("AlertId cannot be empty.", nameof(alertId));

        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(systemId));

        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        AlertId = alertId;
        SystemId = systemId;
        ComponentId = componentId;
        AlertStatus = alertStatus;
        OccurredAt = occurredAt;
        IsGlobalFlagActive = isGlobalFlagActive;
    }

    /// <summary>
    /// Acknowledges the alert and clears the global flag (FR-019, BI-009).
    /// </summary>
    public void Acknowledge(string operatorName, DateTimeOffset acknowledgedAt)
    {
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("OperatorName is required to acknowledge an alert (BI-009).", nameof(operatorName));

        AcknowledgedBy = operatorName;
        AcknowledgedAt = acknowledgedAt;
        IsGlobalFlagActive = false;
    }

    public bool IsAcknowledged => AcknowledgedBy is not null;
}
