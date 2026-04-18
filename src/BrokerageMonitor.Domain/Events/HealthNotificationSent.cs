using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a health summary notification is sent (Email/Teams).
/// </summary>
public sealed record HealthNotificationSent(
    Guid DefinitionId,
    Guid ExecutionId,
    string SystemId,
    NotificationType NotificationType,
    DateTimeOffset OccurredAt) : IDomainEvent;
