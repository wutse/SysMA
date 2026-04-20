using BrokerageMonitor.Application.Messaging;

namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>Parses raw ZeroMQ payload bytes into a <see cref="HeartbeatMessage"/>.</summary>
public interface IHeartbeatMessageParser
{
    /// <summary>
    /// Parses <paramref name="payload"/> as UTF-8 JSON.
    /// Returns <see langword="null"/> and logs a WARNING if parsing fails or required fields
    /// are absent.
    /// </summary>
    HeartbeatMessage? Parse(byte[] payload);
}
