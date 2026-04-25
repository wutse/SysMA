using BrokerageMonitor.Application.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// SMTP email notification service using MailKit with no-auth relay.
/// US-053 (EP-009). FR-013, FR-014.
///
/// Throws <see cref="EmailDeliveryException"/> on any SMTP failure so that
/// callers (Application layer) can write a <c>NotificationDeliveryFailed</c> inbox item.
/// </summary>
public sealed class SmtpEmailNotificationService : IEmailNotificationService
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailNotificationService> _logger;

    public SmtpEmailNotificationService(
        IOptions<SmtpOptions> options,
        ILogger<SmtpEmailNotificationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAlertAsync(AlertEmailRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Recipients.Count == 0)
        {
            _logger.LogDebug("SendAlertAsync: no recipients for system {SystemId}. Skipped.",
                request.SystemId);
            return;
        }

        var subject = $"[告警] {request.SystemName} — {request.ComponentName} ({request.AlertStatus})";
        var body = BuildAlertBody(request);

        await SendAsync(subject, body, request.Recipients, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SendHealthSummaryAsync(
        HealthSummaryEmailRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Recipients.Count == 0)
        {
            _logger.LogDebug(
                "SendHealthSummaryAsync: no recipients for definition {DefinitionId}. Skipped.",
                request.DefinitionId);
            return;
        }

        var statusLabel = request.ExecutionStatus switch
        {
            Domain.ValueObjects.DailyExecutionStatus.Success => "成功",
            Domain.ValueObjects.DailyExecutionStatus.Failed => "失敗",
            Domain.ValueObjects.DailyExecutionStatus.Exempted => "豁免",
            Domain.ValueObjects.DailyExecutionStatus.Missed => "未執行",
            _ => request.ExecutionStatus.ToString()
        };

        var subject = $"[彙整健康] {request.DefinitionName} — {statusLabel} ({request.ExecutionDate:yyyy-MM-dd})";
        var body = BuildHealthSummaryBody(request, statusLabel);

        await SendAsync(subject, body, request.Recipients, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task SendAsync(
        string subject,
        string body,
        IReadOnlyList<string> recipients,
        CancellationToken ct)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress));

            foreach (var recipient in recipients)
                message.To.Add(MailboxAddress.Parse(recipient));

            message.Subject = subject;
            message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                ct).ConfigureAwait(false);

            // No-auth relay (US-053)
            await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Email sent: Subject='{Subject}', Recipients={Count}.", subject, recipients.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP delivery failed for subject '{Subject}'.", subject);
            throw new EmailDeliveryException($"Failed to send email: {subject}", ex);
        }
    }

    private static string BuildAlertBody(AlertEmailRequest request)
    {
        return $"""
            <html><body>
            <h3>⚠️ 告警通知</h3>
            <table border="1" cellpadding="6" cellspacing="0">
              <tr><th>系統</th><td>{Encode(request.SystemName)} ({Encode(request.SystemId)})</td></tr>
              <tr><th>元件</th><td>{Encode(request.ComponentName)} ({Encode(request.ComponentId)})</td></tr>
              <tr><th>狀態</th><td>{request.AlertStatus}</td></tr>
              <tr><th>發生時間</th><td>{request.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}</td></tr>
              <tr><th>告警 ID</th><td>{request.AlertId}</td></tr>
            </table>
            <p>請登入監控系統查看詳情。</p>
            </body></html>
            """;
    }

    private static string BuildHealthSummaryBody(HealthSummaryEmailRequest request, string statusLabel)
    {
        var failedSection = request.FailedComponents.Count > 0
            ? $"<p><strong>未完成元件：</strong>{Encode(string.Join(", ", request.FailedComponents))}</p>"
            : string.Empty;

        return $"""
            <html><body>
            <h3>📊 彙整健康報告</h3>
            <table border="1" cellpadding="6" cellspacing="0">
              <tr><th>規則名稱</th><td>{Encode(request.DefinitionName)}</td></tr>
              <tr><th>系統</th><td>{Encode(request.SystemName)} ({Encode(request.SystemId)})</td></tr>
              <tr><th>執行日期</th><td>{request.ExecutionDate:yyyy-MM-dd}</td></tr>
              <tr><th>執行狀態</th><td>{statusLabel}</td></tr>
              <tr><th>執行 ID</th><td>{request.ExecutionId}</td></tr>
            </table>
            {failedSection}
            <p>請登入監控系統查看詳情。</p>
            </body></html>
            """;
    }

    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
