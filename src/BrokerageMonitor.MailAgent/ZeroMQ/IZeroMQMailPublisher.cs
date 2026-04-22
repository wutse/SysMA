namespace BrokerageMonitor.MailAgent.ZeroMQ;

/// <summary>
/// Abstraction over the ZeroMQ PUB socket used to publish
/// <see cref="Models.MailRelayMessage"/> payloads to the broker.
/// FR-048.
/// </summary>
public interface IZeroMQMailPublisher : IDisposable
{
    /// <summary>
    /// Publishes <paramref name="jsonPayload"/> to the <c>mailrelay</c> topic.
    /// On connection failure, logs at ERROR level and retries on the next call.
    /// </summary>
    void Publish(string jsonPayload);
}
