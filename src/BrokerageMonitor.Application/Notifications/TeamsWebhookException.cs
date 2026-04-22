namespace BrokerageMonitor.Application.Notifications;

/// <summary>
/// Thrown when a Teams webhook notification fails to deliver.
/// Infrastructure implementations must throw this on any HTTP or serialization failure.
/// Callers in the Application layer catch this and write a
/// <c>NotificationDeliveryFailed</c> inbox item (FR-013, FR-014, US-054).
/// </summary>
public sealed class TeamsWebhookException : Exception
{
    public TeamsWebhookException(string message) : base(message) { }

    public TeamsWebhookException(string message, Exception innerException)
        : base(message, innerException) { }
}
