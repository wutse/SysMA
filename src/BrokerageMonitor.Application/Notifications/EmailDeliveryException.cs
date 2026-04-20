namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Thrown when an email cannot be delivered via SMTP.
/// Infrastructure implementations must throw this on any SMTP failure.
/// Callers in the Application layer catch this and write a
/// <c>NotificationDeliveryFailed</c> inbox item (FR-013, FR-014, US-031).
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message) { }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException) { }
}
