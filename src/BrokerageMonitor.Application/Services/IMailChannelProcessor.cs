using BrokerageMonitor.Application.Messaging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Processes incoming ZeroMQ mail-relay messages forwarded by the Mail Relay Local Agent.
/// Iterates all active components with a <c>MailParsingRule</c> and applies From + Subject
/// matching; on match, keyword evaluation (failure-first, BI-016) maps the result to a
/// <c>ComponentStatus</c> that flows through the identical downstream pipeline as a heartbeat (BI-017).
/// FR-050, FR-051.
/// </summary>
public interface IMailChannelProcessor
{
    Task ProcessAsync(MailRelayMessage message, CancellationToken ct = default);
}
