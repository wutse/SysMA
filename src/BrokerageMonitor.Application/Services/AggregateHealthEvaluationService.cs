using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Evaluates aggregate health definitions against component progress (US-049, US-050, EP-009).
///
/// <list type="bullet">
///   <item><see cref="UpdateComponentProgressAsync"/>: event-driven progress tracking (FR-034)</item>
///   <item><see cref="EvaluateDefinitionAsync"/>: deadline evaluation (FR-045, BI-006, BI-008, BI-013)</item>
/// </list>
/// </summary>
public sealed class AggregateHealthEvaluationService : IAggregateHealthEvaluationService
{
    private readonly IHealthMonitorDefinitionRepository _definitionRepo;
    private readonly IDailyExecutionRepository _executionRepo;
    private readonly IMonitoredSystemRepository _systemRepo;
    private readonly IComponentStateRepository _componentStateRepo;
    private readonly INotificationInboxRepository _inboxRepo;
    private readonly IEmailNotificationService _email;
    private readonly ITeamsNotificationService _teams;
    private readonly IRealtimeNotificationService _realtime;
    private readonly IMonitorBroadcaster _broadcaster;
    private readonly ILogger<AggregateHealthEvaluationService> _logger;

    public AggregateHealthEvaluationService(
        IHealthMonitorDefinitionRepository definitionRepo,
        IDailyExecutionRepository executionRepo,
        IMonitoredSystemRepository systemRepo,
        IComponentStateRepository componentStateRepo,
        INotificationInboxRepository inboxRepo,
        IEmailNotificationService email,
        ITeamsNotificationService teams,
        IRealtimeNotificationService realtime,
        IMonitorBroadcaster broadcaster,
        ILogger<AggregateHealthEvaluationService> logger)
    {
        _definitionRepo = definitionRepo;
        _executionRepo = executionRepo;
        _systemRepo = systemRepo;
        _componentStateRepo = componentStateRepo;
        _inboxRepo = inboxRepo;
        _email = email;
        _teams = teams;
        _realtime = realtime;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // US-049: Event-driven progress tracking (FR-034)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task UpdateComponentProgressAsync(
        ComponentStatusChanged evt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Load only active definitions that watch this specific component (avoids full table scan)
        var watchingDefinitions = await _definitionRepo
            .GetByWatchedComponentAsync(evt.ComponentId, ct).ConfigureAwait(false);

        if (watchingDefinitions.Count == 0)
            return;

        foreach (var definition in watchingDefinitions)
        {
            var execution = await _executionRepo
                .GetByDefinitionAndDateAsync(definition.DefinitionId, today, ct)
                .ConfigureAwait(false);

            if (execution is null || execution.Status != DailyExecutionStatus.InProgress)
                continue;

            var watchedComponent = definition.WatchedComponents
                .First(w => w.ComponentId == evt.ComponentId);

            if (!HasMetCompletionConditionFromEvent(watchedComponent, evt))
                continue;

            // Record progress in-memory and persist
            execution.AddCompletedComponent(evt.ComponentId);
            await _executionRepo.AddCompletedComponentAsync(
                execution.ExecutionId, evt.ComponentId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Component {ComponentId} progress recorded for execution {ExecutionId}.",
                evt.ComponentId, execution.ExecutionId);

            await NotifyExecutionUpdatedAsync(execution.ExecutionId, ct).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------
    // US-050: Deadline evaluation (FR-045, BI-006, BI-008, BI-013)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task EvaluateDefinitionAsync(Guid definitionId, CancellationToken ct = default)
    {
        var definition = await _definitionRepo.GetByIdAsync(definitionId, ct).ConfigureAwait(false);
        if (definition is null)
        {
            _logger.LogWarning(
                "EvaluateDefinitionAsync: definition {DefinitionId} not found.", definitionId);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var execution = await _executionRepo
            .GetByDefinitionAndDateAsync(definitionId, today, ct)
            .ConfigureAwait(false);

        if (execution is null)
        {
            _logger.LogWarning(
                "No DailyExecution found for definition {DefinitionId} on {Date}.", definitionId, today);
            return;
        }

        // BI-013: terminal state cannot be overwritten
        if (execution.IsTerminal)
        {
            _logger.LogDebug(
                "Execution {ExecutionId} already in terminal state {Status}. Skipping evaluation (BI-013).",
                execution.ExecutionId, execution.Status);
            return;
        }

        var system = await _systemRepo.GetByIdAsync(definition.SystemId, ct).ConfigureAwait(false);

        // FR-016 / BI-006: maintenance mode → Exempted
        if (system?.IsMaintenanceActive == true)
        {
            await FinalizeExecutionAsync(
                execution, definition, system, DailyExecutionStatus.Exempted,
                failedComponents: [],
                ct).ConfigureAwait(false);
            return;
        }

        // BI-008: evaluate each watched component
        var componentIds = definition.WatchedComponents.Select(w => w.ComponentId).ToList();
        var states = await _componentStateRepo.GetByComponentIdsAsync(componentIds, ct).ConfigureAwait(false);
        var stateMap = states.ToDictionary(s => s.ComponentId, StringComparer.OrdinalIgnoreCase);

        var failedComponents = new List<string>();

        foreach (var watched in definition.WatchedComponents)
        {
            stateMap.TryGetValue(watched.ComponentId, out var state);

            if (!HasMetCompletionConditionAtDeadline(watched, state, execution))
                failedComponents.Add(watched.ComponentId);
        }

        var terminalStatus = failedComponents.Count == 0
            ? DailyExecutionStatus.Success
            : DailyExecutionStatus.Failed;

        await FinalizeExecutionAsync(
            execution, definition, system, terminalStatus, failedComponents, ct)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Determines whether a component has met its completion condition based on a status-changed event.
    /// Used for incremental (pre-deadline) progress tracking (FR-034).
    /// </summary>
    private static bool HasMetCompletionConditionFromEvent(
        WatchedComponent watched,
        ComponentStatusChanged evt)
    {
        return watched.ComponentType switch
        {
            // ScheduledJob: eligible when transitioning to Completed (not Lost)
            ComponentType.ScheduledJob =>
                evt.NewStatus == ComponentStatus.Completed &&
                evt.NewStatus != ComponentStatus.Lost,

            // Service: eligible when transitioning to Normal with no sub-indicators in degraded state
            ComponentType.Service =>
                evt.NewStatus == ComponentStatus.Normal,

            _ => false
        };
    }

    /// <summary>
    /// Evaluates BI-008 completion conditions at deadline time using persisted component state.
    /// </summary>
    private static bool HasMetCompletionConditionAtDeadline(
        WatchedComponent watched,
        ComponentState? state,
        DailyExecution execution)
    {
        if (state is null)
            return false;

        return watched.ComponentType switch
        {
            // ScheduledJob: must be in CompletedComponents (received Completed before deadline) AND not Lost now
            ComponentType.ScheduledJob =>
                execution.CompletedComponents.Contains(watched.ComponentId, StringComparer.OrdinalIgnoreCase) &&
                state.Status != ComponentStatus.Lost,

            // Service: must be Normal now AND all sub-indicators Normal
            ComponentType.Service =>
                state.Status == ComponentStatus.Normal &&
                state.SubIndicators.All(si => si.Status == SubIndicatorStatus.Normal),

            _ => false
        };
    }

    private async Task FinalizeExecutionAsync(
        DailyExecution execution,
        HealthMonitorDefinition definition,
        MonitoredSystem? system,
        DailyExecutionStatus terminalStatus,
        IReadOnlyList<string> failedComponents,
        CancellationToken ct)
    {
        var evaluatedAt = DateTimeOffset.UtcNow;
        execution.Complete(terminalStatus, evaluatedAt, failedComponents);

        DateTimeOffset? notificationSentAt = null;
        var notificationType = TerminalStatusToNotificationType(terminalStatus);

        // FR-013: Success notification is ALWAYS sent (never conditional).
        // FR-014: SendOnFailure controls ONLY the failure notification channel.
        // Exempted executions never trigger external notifications.
        bool shouldSendExternal = terminalStatus switch
        {
            DailyExecutionStatus.Success => true,                    // FR-013: always
            DailyExecutionStatus.Failed => definition.SendOnFailure, // FR-014: conditional
            _ => false                     // Exempted / other
        };

        if (shouldSendExternal)
        {
            notificationSentAt = await SendNotificationsAsync(execution, definition, system, failedComponents, ct)
                .ConfigureAwait(false);
        }

        await _executionRepo.UpdateStatusAsync(
            execution.ExecutionId,
            terminalStatus,
            evaluatedAt,
            failedComponents,
            notificationSentAt,
            ct).ConfigureAwait(false);

        // FR-020: NotificationInbox always written, regardless of SendOnFailure flag
        await WriteInboxItemAsync(execution, definition, notificationType, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "DailyExecution {ExecutionId} finalized as {Status} for definition {DefinitionId}.",
            execution.ExecutionId, terminalStatus, definition.DefinitionId);

        await NotifyExecutionUpdatedAsync(execution.ExecutionId, ct).ConfigureAwait(false);
        _broadcaster.PublishHealthNotificationReceived(
            definition.DefinitionId, execution.ExecutionId, notificationType);
    }

    private async Task<DateTimeOffset?> SendNotificationsAsync(
        DailyExecution execution,
        HealthMonitorDefinition definition,
        MonitoredSystem? system,
        IReadOnlyList<string> failedComponents,
        CancellationToken ct)
    {
        if (definition.EmailRecipients.Count == 0 && definition.TeamsWebhookUrl is null)
            return null;

        var request = new HealthSummaryEmailRequest(
            DefinitionId: definition.DefinitionId,
            ExecutionId: execution.ExecutionId,
            DefinitionName: definition.Name,
            SystemId: definition.SystemId,
            SystemName: system?.Name ?? definition.SystemId,
            ExecutionStatus: execution.Status,
            ExecutionDate: execution.ExecutionDate,
            FailedComponents: failedComponents,
            Recipients: definition.EmailRecipients.Select(e => e.Value).ToList());

        DateTimeOffset? notificationSentAt = null;

        // Send Email
        if (definition.EmailRecipients.Count > 0)
        {
            try
            {
                await _email.SendHealthSummaryAsync(request, ct).ConfigureAwait(false);
                notificationSentAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Health summary email sent for execution {ExecutionId}.", execution.ExecutionId);
            }
            catch (EmailDeliveryException ex)
            {
                _logger.LogError(ex,
                    "Email delivery failed for execution {ExecutionId}. Writing DeliveryFailed inbox item.",
                    execution.ExecutionId);

                await WriteDeliveryFailedItemAsync(execution, definition, ct).ConfigureAwait(false);
            }
        }

        // Send Teams
        if (!string.IsNullOrWhiteSpace(definition.TeamsWebhookUrl))
        {
            try
            {
                await _teams.SendHealthSummaryAsync(request, definition.TeamsWebhookUrl, ct)
                    .ConfigureAwait(false);
                notificationSentAt ??= DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Teams notification sent for execution {ExecutionId}.", execution.ExecutionId);
            }
            catch (TeamsWebhookException ex)
            {
                _logger.LogError(ex,
                    "Teams webhook failed for execution {ExecutionId}. Writing DeliveryFailed inbox item.",
                    execution.ExecutionId);

                await WriteDeliveryFailedItemAsync(execution, definition, ct).ConfigureAwait(false);
            }
        }

        return notificationSentAt;
    }

    private async Task WriteInboxItemAsync(
        DailyExecution execution,
        HealthMonitorDefinition definition,
        NotificationType notificationType,
        CancellationToken ct)
    {
        var title = notificationType switch
        {
            NotificationType.HealthSuccess => $"[成功] {definition.Name} ({execution.ExecutionDate:yyyy-MM-dd})",
            NotificationType.HealthFailure => $"[失敗] {definition.Name} ({execution.ExecutionDate:yyyy-MM-dd})",
            NotificationType.HealthExempted => $"[豁免] {definition.Name} ({execution.ExecutionDate:yyyy-MM-dd})",
            _ => $"{definition.Name} ({execution.ExecutionDate:yyyy-MM-dd})"
        };

        var failedList = execution.FailedComponents.Count > 0
            ? $" 失敗元件：{string.Join(", ", execution.FailedComponents)}"
            : string.Empty;

        var inboxItem = new NotificationInboxItem(
            inboxItemId: Guid.NewGuid(),
            definitionId: definition.DefinitionId,
            executionId: execution.ExecutionId,
            title: title,
            body: $"系統：{definition.SystemId}{failedList}",
            notificationType: notificationType,
            sentAt: DateTimeOffset.UtcNow);

        await _inboxRepo.AddAsync(inboxItem, ct).ConfigureAwait(false);
    }

    private async Task WriteDeliveryFailedItemAsync(
        DailyExecution execution,
        HealthMonitorDefinition definition,
        CancellationToken ct)
    {
        var inboxItem = new NotificationInboxItem(
            inboxItemId: Guid.NewGuid(),
            definitionId: definition.DefinitionId,
            executionId: execution.ExecutionId,
            title: $"[通知失敗] {definition.Name} ({execution.ExecutionDate:yyyy-MM-dd})",
            body: $"系統：{definition.SystemId} — 通知發送失敗，請手動檢查。",
            notificationType: NotificationType.NotificationDeliveryFailed,
            sentAt: DateTimeOffset.UtcNow);

        await _inboxRepo.AddAsync(inboxItem, ct).ConfigureAwait(false);
    }

    private async Task NotifyExecutionUpdatedAsync(Guid executionId, CancellationToken ct)
    {
        try
        {
            await _realtime.NotifyDailyExecutionUpdatedAsync(executionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push failed for execution {ExecutionId}.", executionId);
        }
    }

    private static NotificationType TerminalStatusToNotificationType(DailyExecutionStatus status) =>
        status switch
        {
            DailyExecutionStatus.Success => NotificationType.HealthSuccess,
            DailyExecutionStatus.Failed => NotificationType.HealthFailure,
            DailyExecutionStatus.Exempted => NotificationType.HealthExempted,
            _ => NotificationType.HealthFailure
        };
}
