namespace BrokerageMonitor.MailAgent.Outlook;

/// <summary>
/// Abstraction over Outlook mailbox reading to allow unit testing without COM interop.
/// FR-048.
/// </summary>
public interface IOutlookMailReader
{
    /// <summary>
    /// Reads all unread items from the configured mailbox folder, marks each item as read,
    /// and returns them as a snapshot list.
    /// Exceptions are logged at ERROR level and an empty list is returned — the polling
    /// loop must not be terminated by individual read failures.
    /// </summary>
    IReadOnlyList<RawMailItem> ReadUnreadMails();
}
