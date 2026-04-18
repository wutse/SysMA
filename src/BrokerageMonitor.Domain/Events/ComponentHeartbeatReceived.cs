using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a ZeroMQ heartbeat message is received for a component.
/// </summary>
public sealed record ComponentHeartbeatReceived(
    string ComponentId,
    string SystemId,
    ComponentStatus ReportedStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
