using BrokerageMonitor.Application.Notifications;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// Sends Microsoft Teams health-summary notifications via an Adaptive Card webhook.
/// US-054 (EP-009). FR-013, FR-014.
///
/// Throws <see cref="TeamsWebhookException"/> on any HTTP or serialization failure.
/// Callers are responsible for writing a <c>NotificationDeliveryFailed</c> inbox item.
/// </summary>
public sealed class TeamsNotificationService : ITeamsNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TeamsNotificationService> _logger;

    public TeamsNotificationService(
        HttpClient httpClient,
        ILogger<TeamsNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendHealthSummaryAsync(
        HealthSummaryEmailRequest request,
        string webhookUrl,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new ArgumentException("WebhookUrl cannot be empty.", nameof(webhookUrl));

        var card = BuildAdaptiveCard(request);

        try
        {
            var response = await _httpClient
                .PostAsJsonAsync(webhookUrl, card, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError(
                    "Teams webhook returned {StatusCode} for definition {DefinitionId}. Body: {Body}",
                    response.StatusCode, request.DefinitionId, body);
                throw new TeamsWebhookException(
                    $"Teams webhook returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            _logger.LogInformation(
                "Teams notification sent for definition {DefinitionId} execution {ExecutionId}.",
                request.DefinitionId, request.ExecutionId);
        }
        catch (TeamsWebhookException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Teams webhook request failed for definition {DefinitionId}.", request.DefinitionId);
            throw new TeamsWebhookException(
                $"Teams webhook request failed for definition {request.DefinitionId}", ex);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static object BuildAdaptiveCard(HealthSummaryEmailRequest request)
    {
        var statusLabel = request.ExecutionStatus switch
        {
            Domain.ValueObjects.DailyExecutionStatus.Success => "✅ 成功",
            Domain.ValueObjects.DailyExecutionStatus.Failed => "❌ 失敗",
            Domain.ValueObjects.DailyExecutionStatus.Exempted => "⏸️ 豁免",
            Domain.ValueObjects.DailyExecutionStatus.Missed => "⚠️ 未執行",
            _ => request.ExecutionStatus.ToString()
        };

        var facts = new List<object>
        {
            new { title = "規則名稱", value = request.DefinitionName },
            new { title = "系統", value = $"{request.SystemName} ({request.SystemId})" },
            new { title = "執行日期", value = request.ExecutionDate.ToString("yyyy-MM-dd") },
            new { title = "執行狀態", value = statusLabel }
        };

        if (request.FailedComponents.Count > 0)
        {
            facts.Add(new
            {
                title = "未完成元件",
                value = string.Join(", ", request.FailedComponents)
            });
        }

        // Microsoft Teams Incoming Webhook format (MessageCard / Adaptive Card wrapper)
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = $"彙整健康報告 — {statusLabel}",
                                weight = "Bolder",
                                size = "Medium"
                            },
                            new
                            {
                                type = "FactSet",
                                facts
                            }
                        }
                    }
                }
            }
        };
    }
}
