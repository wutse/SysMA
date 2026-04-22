namespace BrokerageMonitor.MailAgent.Outlook;

/// <summary>
/// Raw mail item data extracted from an Outlook folder.
/// Immutable snapshot — Outlook COM objects are released before this record is returned.
/// </summary>
public sealed record RawMailItem(
    string From,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt);
