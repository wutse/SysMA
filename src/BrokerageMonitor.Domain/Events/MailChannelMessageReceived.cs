namespace BrokerageMonitor.Domain.Events;

/// <summary>
/// Raised when a mail relay message arrives from the Local Agent via ZeroMQ (FR-050/051).
/// MailChannelProcessor matches against MailParsingRule and maps to ComponentStatus.
/// </summary>
public sealed record MailChannelMessageReceived(
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt,
    DateTimeOffset OccurredAt) : IDomainEvent;
