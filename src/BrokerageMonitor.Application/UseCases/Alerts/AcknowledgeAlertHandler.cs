using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Alerts;

/// <summary>
/// Handles <see cref="AcknowledgeAlertCommand"/> to acknowledge all active alerts
/// for a monitored system (US-032).
///
/// Business rules:
/// <list type="bullet">
///   <item>OperatorName must not be empty (BI-009).</item>
///   <item>SystemId must correspond to a known system.</item>
///   <item>Calls <c>AcknowledgeBySystemAsync</c> to clear the system-wide GlobalFlag (FR-019).</item>
///   <item>Raises <see cref="AlertAcknowledged"/> domain event.</item>
///   <item>Writes an operator-action audit log entry.</item>
///   <item>Pushes <c>OnAlertAcknowledged</c> SignalR notification (FR-018).</item>
/// </list>
/// </summary>
public sealed class AcknowledgeAlertHandler
{
    private readonly IAlertRecordRepository _alertRepository;
    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly IRealtimeNotificationService _realtimeNotification;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AcknowledgeAlertHandler> _logger;

    public AcknowledgeAlertHandler(
        IAlertRecordRepository alertRepository,
        IMonitoredSystemRepository systemRepository,
        IAuditLogger auditLogger,
        IRealtimeNotificationService realtimeNotification,
        TimeProvider timeProvider,
        ILogger<AcknowledgeAlertHandler> logger)
    {
        _alertRepository = alertRepository;
        _systemRepository = systemRepository;
        _auditLogger = auditLogger;
        _realtimeNotification = realtimeNotification;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes the acknowledge-alert workflow.
    /// </summary>
    public async Task HandleAsync(AcknowledgeAlertCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OperatorName))
            throw new ArgumentException(
                "OperatorName is required to acknowledge an alert (BI-009).",
                nameof(command));

        if (string.IsNullOrWhiteSpace(command.SystemId))
            throw new ArgumentException(
                "SystemId cannot be empty.",
                nameof(command));

        var system = await _systemRepository.GetByIdAsync(command.SystemId, ct);
        if (system is null)
        {
            _logger.LogWarning(
                "ERR_SYSTEM_NOT_FOUND: Cannot acknowledge alert — system {SystemId} not found.",
                command.SystemId);
            return;
        }

        var now = _timeProvider.GetUtcNow();

        // Clear the system-wide GlobalFlag for all unacknowledged alerts (FR-019)
        await _alertRepository.AcknowledgeBySystemAsync(command.SystemId, command.OperatorName, now, ct);

        _logger.LogInformation(
            "Alert acknowledged: System={SystemId}, Operator={OperatorName}",
            command.SystemId, command.OperatorName);

        // Write audit log
        await _auditLogger.LogOperatorActionAsync(
            systemId: command.SystemId,
            componentId: null,
            actionType: "AlertAcknowledged",
            operatorName: command.OperatorName,
            reason: null,
            occurredAt: now,
            ct: ct);

        // SignalR push (FR-018)
        await _realtimeNotification.NotifyAlertAcknowledgedAsync(command.SystemId, ct);
    }
}
