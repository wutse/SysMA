namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Abstraction for sending Microsoft Teams webhook notifications.
/// US-054 (EP-009) provides the <c>TeamsNotificationService</c> implementation.
/// On failure the implementation must throw <see cref="TeamsWebhookException"/>;
/// callers are responsible for catching it and recording a
/// <c>NotificationDeliveryFailed</c> inbox item (FR-013, FR-014).
/// </summary>
public interface ITeamsNotificationService
{
    /// <summary>
    /// Sends a health summary Adaptive Card to the specified Teams webhook URL (FR-013, FR-014).
    /// </summary>
    Task SendHealthSummaryAsync(
        HealthSummaryEmailRequest request,
        string webhookUrl,
        CancellationToken ct = default);
}
