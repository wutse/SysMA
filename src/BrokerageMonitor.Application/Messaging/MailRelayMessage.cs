namespace BrokerageMonitor.Application.Messaging;

/// <summary>
/// DTO representing a ZeroMQ mail-relay message payload (Design Doc §6.2).
/// Topic frame is always <c>mailrelay</c>.
/// messageType is always "MailRelay".
/// </summary>
public sealed record MailRelayMessage(
    string MessageType,
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt);
