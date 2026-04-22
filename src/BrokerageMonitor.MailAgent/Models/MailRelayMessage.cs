namespace BrokerageMonitor.MailAgent.Models;

/// <summary>
/// Outgoing ZeroMQ message DTO produced by the Mail Relay Local Agent.
/// Published to topic <c>mailrelay</c> as UTF-8 JSON.
/// JSON contract defined in Design Doc §6.2.
/// FR-048.
/// </summary>
public sealed record MailRelayMessage(
    string MessageType,
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt);
