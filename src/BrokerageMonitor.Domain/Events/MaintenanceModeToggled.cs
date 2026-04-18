namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when maintenance mode is toggled on or off for a system (FR-032).
/// </summary>
public sealed record MaintenanceModeToggled(
    string SystemId,
    bool IsActive,
    string OperatorName,
    DateTimeOffset OccurredAt) : IDomainEvent;
