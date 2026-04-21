namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Push payload for a received aggregate-health notification.
/// Sent to the client via <c>OnHealthNotificationReceived</c> SignalR push (FR-020).
/// </summary>
public sealed record NotificationInboxItemDto(
    Guid InboxItemId,
    string Title,
    string Body,
    string NotificationType,
    DateTimeOffset SentAt,
    bool IsRead);
