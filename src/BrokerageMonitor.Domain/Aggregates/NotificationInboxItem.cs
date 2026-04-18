using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing an item in the notification inbox.
/// FR-015, FR-020, FR-024
/// </summary>
public sealed class NotificationInboxItem
{
    public Guid InboxItemId { get; private init; }
    public Guid DefinitionId { get; private init; }
    public Guid? ExecutionId { get; private init; }
    public string Title { get; private init; }
    public string Body { get; private init; }
    public NotificationType NotificationType { get; private init; }
    public DateTimeOffset SentAt { get; private init; }
    public bool IsRead { get; private set; }

    // Required for Dapper materialization
    private NotificationInboxItem()
    {
        Title = null!;
        Body = null!;
    }

    public NotificationInboxItem(
        Guid inboxItemId,
        Guid definitionId,
        Guid? executionId,
        string title,
        string body,
        NotificationType notificationType,
        DateTimeOffset sentAt)
    {
        if (inboxItemId == Guid.Empty)
            throw new ArgumentException("InboxItemId cannot be empty.", nameof(inboxItemId));

        if (definitionId == Guid.Empty)
            throw new ArgumentException("DefinitionId cannot be empty.", nameof(definitionId));

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body cannot be empty.", nameof(body));

        InboxItemId = inboxItemId;
        DefinitionId = definitionId;
        ExecutionId = executionId;
        Title = title;
        Body = body;
        NotificationType = notificationType;
        SentAt = sentAt;
        IsRead = false;
    }

    public void MarkAsRead() => IsRead = true;
}
