using System.Text.Json;
using BrokerageMonitor.MailAgent.Models;
using BrokerageMonitor.MailAgent.Options;
using BrokerageMonitor.MailAgent.Outlook;
using BrokerageMonitor.MailAgent.ZeroMQ;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BrokerageMonitor.MailAgent.Workers;

/// <summary>
/// Background service that polls the Outlook mailbox every <c>PollIntervalSeconds</c>,
/// encapsulates each unread mail as a <see cref="MailRelayMessage"/>, and publishes it
/// to the ZeroMQ broker via <see cref="IZeroMQMailPublisher"/>.
/// Supports graceful shutdown via <see cref="CancellationToken"/>.
/// FR-048, US-058.
/// </summary>
public sealed class MailRelayWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOutlookMailReader _mailReader;
    private readonly IZeroMQMailPublisher _publisher;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxBodyCharacters;
    private readonly ILogger<MailRelayWorker> _logger;

    public MailRelayWorker(
        IOutlookMailReader mailReader,
        IZeroMQMailPublisher publisher,
        IOptions<MailAgentOptions> options,
        ILogger<MailRelayWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _mailReader = mailReader;
        _publisher = publisher;
        _pollInterval = TimeSpan.FromSeconds(
            Math.Max(1, options.Value.PollIntervalSeconds));
        _maxBodyCharacters = options.Value.MaxBodyCharacters;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MailRelayWorker started. Poll interval: {Interval} s.", _pollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PollAndPublishAsync(stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("MailRelayWorker stopped.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private Task PollAndPublishAsync(CancellationToken ct)
    {
        IReadOnlyList<RawMailItem> mails;

        try
        {
            mails = _mailReader.ReadUnreadMails();
        }
        catch (Exception ex)
        {
            // OutlookMailReader is expected to swallow and log its own errors;
            // this catch is a safety net to ensure the loop survives.
            _logger.LogError(ex, "MailRelayWorker: unexpected error reading mails — skipping this cycle.");
            return Task.CompletedTask;
        }

        foreach (var mail in mails)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var message = new MailRelayMessage(
                    MessageType: "MailRelay",
                    From: mail.From,
                    Subject: mail.Subject,
                    Body: TruncateBody(mail.Body),
                    ReceivedAt: mail.ReceivedAt);

                var json = JsonSerializer.Serialize(message, JsonOptions);
                _publisher.Publish(json);

                _logger.LogInformation(
                    "MailRelayWorker: published mail from '{From}' subject '{Subject}'.",
                    mail.From, mail.Subject);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "MailRelayWorker: failed to publish mail from '{From}' — continuing.",
                    mail.From);
            }
        }

        return Task.CompletedTask;
    }

    private string TruncateBody(string body)
    {
        if (_maxBodyCharacters <= 0 || body.Length <= _maxBodyCharacters)
            return body;

        _logger.LogWarning(
            "MailRelayWorker: email body ({Length} chars) exceeds MaxBodyCharacters ({Max}); truncating.",
            body.Length, _maxBodyCharacters);

        return body[.._maxBodyCharacters];
    }
}
