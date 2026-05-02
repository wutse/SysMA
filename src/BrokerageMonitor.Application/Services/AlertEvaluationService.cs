using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Evaluates whether a component status change should trigger a new alert (US-031).
///
/// Business rules enforced:
/// <list type="bullet">
///   <item>Only <c>Lost</c>, <c>Error</c>, and <c>Warning</c> statuses trigger alerts (FR-010).</item>
///   <item>Alerts are suppressed outside the market session window (FR-030).</item>
///   <item>Alerts are suppressed when the system is in maintenance mode (BI-006).</item>
///   <item>A second alert is not raised while an unacknowledged alert exists for the same system (BI-007).</item>
///   <item>On alert trigger: persists <see cref="AlertRecord"/>, sends email (FR-037),
///         and pushes a real-time notification (FR-010).</item>
///   <item>On email delivery failure: logs the error and writes a
///         <c>NotificationDeliveryFailed</c> inbox item (US-031 acceptance criteria).</item>
/// </list>
/// </summary>
public sealed class AlertEvaluationService : IAlertEvaluationService
{
    /// <summary>
    /// Sentinel DefinitionId used in the notification inbox when an alert email fails.
    /// Alert notifications have no associated HealthMonitorDefinition; this non-empty
    /// GUID satisfies the domain invariant while making the item identifiable.
    /// </summary>
    public static readonly Guid AlertEmailFailureDefinitionId =
        new("00000000-0000-0000-0000-000000000001");

    private static readonly IReadOnlySet<ComponentStatus> AlertableStatuses =
        new HashSet<ComponentStatus> { ComponentStatus.Lost, ComponentStatus.Error, ComponentStatus.Warning };

    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IAlertRecordRepository _alertRepository;
    private readonly IEmailNotificationService _emailService;
    private readonly IRealtimeNotificationService _realtimeNotification;
    private readonly INotificationInboxRepository _inboxRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        IMonitoredSystemRepository systemRepository,
        IMonitoredComponentRepository componentRepository,
        IAlertRecordRepository alertRepository,
        IEmailNotificationService emailService,
        IRealtimeNotificationService realtimeNotification,
        INotificationInboxRepository inboxRepository,
        TimeProvider timeProvider,
        ILogger<AlertEvaluationService> logger)
    {
        _systemRepository = systemRepository;
        _componentRepository = componentRepository;
        _alertRepository = alertRepository;
        _emailService = emailService;
        _realtimeNotification = realtimeNotification;
        _inboxRepository = inboxRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EvaluateAsync(ComponentStatusChanged evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // 1. Only alert on actionable statuses (FR-010)
        if (!AlertableStatuses.Contains(evt.NewStatus))
            return;

        // 2. Load system configuration
        var system = await _systemRepository.GetByIdAsync(evt.SystemId, ct);
        if (system is null)
        {
            _logger.LogWarning(
                "ERR_SYSTEM_NOT_FOUND: Cannot evaluate alert — system {SystemId} not found.",
                evt.SystemId);
            return;
        }

        // 3. Suppress alert in maintenance mode (BI-006)
        if (system.IsMaintenanceActive)
        {
            _logger.LogDebug(
                "Alert suppressed for system {SystemId} / component {ComponentId}: maintenance mode active.",
                evt.SystemId, evt.ComponentId);
            return;
        }

        // 4. Suppress alert outside market session (FR-030)
        if (!system.MarketSession.IsWithinSession(evt.OccurredAt))
        {
            _logger.LogDebug(
                "Alert suppressed for system {SystemId} / component {ComponentId}: outside market session.",
                evt.SystemId, evt.ComponentId);
            return;
        }

        // 5. Suppress duplicate alert while GlobalFlag is active (BI-007)
        if (await _alertRepository.HasUnacknowledgedAlertAsync(evt.SystemId, ct))
        {
            _logger.LogDebug(
                "Alert suppressed for system {SystemId} / component {ComponentId}: unacknowledged alert already active.",
                evt.SystemId, evt.ComponentId);
            return;
        }

        // 6. Create and persist the alert record
        var alertId = Guid.NewGuid();
        var alert = new AlertRecord(alertId, evt.SystemId, evt.ComponentId, evt.NewStatus, evt.OccurredAt);
        await _alertRepository.AddAsync(alert, ct);

        _logger.LogInformation(
            "Alert triggered: AlertId={AlertId}, System={SystemId}, Component={ComponentId}, Status={Status}",
            alertId, evt.SystemId, evt.ComponentId, evt.NewStatus);

        // 7. Send alert email (FR-010, FR-037) — failure writes to NotificationDeliveryFailed inbox
        await SendAlertEmailAsync(system, evt, alertId, ct);

        // 8. Real-time SignalR push (FR-010)
        await _realtimeNotification.NotifyAlertTriggeredAsync(evt.SystemId, evt.ComponentId, ct);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task SendAlertEmailAsync(
        Domain.Aggregates.MonitoredSystem system,
        ComponentStatusChanged evt,
        Guid alertId,
        CancellationToken ct)
    {
        var recipients = system.AlertRecipients
            .Select(r => r.Value)
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogDebug(
                "No alert email recipients configured for system {SystemId}; skipping email.",
                system.SystemId);
            return;
        }

        // Attempt to resolve component name for the email body
        var componentName = await ResolveComponentNameAsync(evt.ComponentId, ct);

        var request = new AlertEmailRequest(
            AlertId: alertId,
            SystemId: system.SystemId,
            SystemName: system.Name,
            ComponentId: evt.ComponentId,
            ComponentName: componentName,
            AlertStatus: evt.NewStatus,
            OccurredAt: evt.OccurredAt,
            Recipients: recipients);

        try
        {
            await _emailService.SendAlertAsync(request, ct);
        }
        catch (EmailDeliveryException ex)
        {
            _logger.LogError(
                ex,
                "ERR_EMAIL_DELIVERY: Alert email delivery failed for AlertId={AlertId}, System={SystemId}.",
                alertId, system.SystemId);

            await WriteEmailFailureInboxItemAsync(alertId, system, evt, ct);
        }
    }

    private async Task<string> ResolveComponentNameAsync(string componentId, CancellationToken ct)
    {
        try
        {
            var component = await _componentRepository.GetByIdAsync(componentId, ct);
            return component?.Name ?? componentId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve component name for {ComponentId}.", componentId);
            return componentId;
        }
    }

    private async Task WriteEmailFailureInboxItemAsync(
        Guid alertId,
        Domain.Aggregates.MonitoredSystem system,
        ComponentStatusChanged evt,
        CancellationToken ct)
    {
        try
        {
            var item = new Domain.Aggregates.NotificationInboxItem(
                inboxItemId: Guid.NewGuid(),
                definitionId: AlertEmailFailureDefinitionId,
                executionId: null,
                title: $"Alert email delivery failed — {system.Name}",
                body: $"Failed to send alert email for component {evt.ComponentId} " +
                      $"(status: {evt.NewStatus}) in system '{system.Name}' " +
                      $"at {evt.OccurredAt:u}. AlertId: {alertId}.",
                notificationType: Domain.ValueObjects.NotificationType.NotificationDeliveryFailed,
                sentAt: _timeProvider.GetUtcNow());

            await _inboxRepository.AddAsync(item, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write NotificationDeliveryFailed inbox item for AlertId={AlertId}.",
                alertId);
        }
    }
}
