namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// Configuration options for the no-auth SMTP relay (US-053).
/// Bound from the <c>Smtp</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 25;
    public bool EnableSsl { get; init; } = false;
    public string FromAddress { get; init; } = "monitor@brokerage.local";
    public string FromDisplayName { get; init; } = "Brokerage Monitor";
}
