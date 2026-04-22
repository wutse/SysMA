namespace BrokerageMonitor.MailAgent.Options;

/// <summary>
/// Strongly-typed configuration options for the Mail Relay Local Agent.
/// Bound from the <c>MailAgent</c> section of <c>appsettings.json</c>.
/// FR-048.
/// </summary>
public sealed class MailAgentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MailAgent";

    /// <summary>
    /// ZeroMQ Broker XSUB endpoint that the PUB socket connects to.
    /// Example: <c>tcp://127.0.0.1:5556</c>.
    /// </summary>
    public string BrokerAddress { get; set; } = "tcp://127.0.0.1:5556";

    /// <summary>
    /// Polling interval in seconds between Outlook mailbox reads.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Outlook mailbox folder name to poll for unread messages.
    /// Example: <c>Inbox</c>.
    /// </summary>
    public string MailboxFolder { get; set; } = "Inbox";
}
