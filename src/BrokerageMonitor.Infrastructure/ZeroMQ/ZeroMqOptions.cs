namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>
/// Strongly-typed options for the ZeroMQ subscriber (bound from <c>ZeroMQ</c> configuration section).
/// </summary>
public sealed class ZeroMqOptions
{
    /// <summary>Section name in appsettings.json.</summary>
    public const string SectionName = "ZeroMQ";

    /// <summary>
    /// Endpoint the <c>ZeroMQSubscriberService</c> connects its XSUB socket to.
    /// Example: <c>tcp://127.0.0.1:5559</c>.
    /// </summary>
    public string BrokerAddress { get; set; } = "tcp://127.0.0.1:5559";
}
