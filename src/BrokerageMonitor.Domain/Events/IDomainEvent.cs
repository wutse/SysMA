namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Marker interface for all domain events.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
