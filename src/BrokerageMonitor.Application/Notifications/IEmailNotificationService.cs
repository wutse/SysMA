namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Abstraction for sending email notifications.
/// US-053 (EP-009) provides the <c>SmtpEmailNotificationService</c> implementation.
/// On send failure the implementation must throw <see cref="EmailDeliveryException"/>;
/// callers are responsible for catching it and recording a
/// <c>NotificationDeliveryFailed</c> inbox item.
/// FR-010, FR-013, FR-014, FR-037.
/// </summary>
public interface IEmailNotificationService
{
    /// <summary>
    /// Sends an alert notification email for a triggered component alert (FR-010, FR-037).
    /// </summary>
    Task SendAlertAsync(AlertEmailRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sends a health summary email for aggregate health evaluation results (FR-013, FR-014).
    /// </summary>
    Task SendHealthSummaryAsync(HealthSummaryEmailRequest request, CancellationToken ct = default);
}
