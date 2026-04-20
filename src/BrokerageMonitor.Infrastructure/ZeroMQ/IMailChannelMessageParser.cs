using BrokerageMonitor.Application.Messaging;

namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>Parses raw ZeroMQ payload bytes into a <see cref="MailRelayMessage"/>.</summary>
public interface IMailChannelMessageParser
{
    /// <summary>
    /// Parses <paramref name="payload"/> as UTF-8 JSON.
    /// Validates that <c>messageType == "MailRelay"</c>.
    /// Returns <see langword="null"/> and logs a WARNING if parsing fails or required fields
    /// are absent.
    /// </summary>
    MailRelayMessage? Parse(byte[] payload);
}
