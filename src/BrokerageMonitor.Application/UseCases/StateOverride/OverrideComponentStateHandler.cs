using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.StateOverride;

/// <summary>
/// Handles <see cref="OverrideComponentStateCommand"/> to manually force a component
/// into a given operational status (US-034).
///
/// Business rules:
/// <list type="bullet">
///   <item>OperatorName must not be empty (BI-009).</item>
///   <item>Reason must not be empty (BI-001).</item>
///   <item>Updates <see cref="ComponentState"/> via repository (FR-005/006/007).</item>
///   <item>Raises <see cref="ComponentStateOverridden"/> domain event (dispatched via
///     <see cref="IDomainEventDispatcher"/> so subscribers, including alert evaluation, are notified).</item>
///   <item>Writes an operator-action audit log entry.</item>
///   <item>Pushes a <c>ComponentStatusChanged</c> SignalR notification so the UI refreshes.</item>
/// </list>
/// </summary>
public sealed class OverrideComponentStateHandler
{
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IComponentStateRepository _stateRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IAuditLogger _auditLogger;
    private readonly IRealtimeNotificationService _realtimeNotification;
    private readonly ILogger<OverrideComponentStateHandler> _logger;

    public OverrideComponentStateHandler(
        IMonitoredComponentRepository componentRepository,
        IComponentStateRepository stateRepository,
        IComponentStateCache stateCache,
        IDomainEventDispatcher eventDispatcher,
        IAuditLogger auditLogger,
        IRealtimeNotificationService realtimeNotification,
        ILogger<OverrideComponentStateHandler> logger)
    {
        _componentRepository = componentRepository;
        _stateRepository = stateRepository;
        _stateCache = stateCache;
        _eventDispatcher = eventDispatcher;
        _auditLogger = auditLogger;
        _realtimeNotification = realtimeNotification;
        _logger = logger;
    }

    /// <summary>Executes the override-component-state workflow.</summary>
    public async Task HandleAsync(OverrideComponentStateCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OperatorName))
            throw new ArgumentException(
                "OperatorName is required to override component state (BI-009).",
                nameof(command));

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new ArgumentException(
                "Reason is required to override component state (BI-001).",
                nameof(command));

        if (string.IsNullOrWhiteSpace(command.ComponentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.SystemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(command));

        var component = await _componentRepository.GetByIdAsync(command.ComponentId, ct);
        if (component is null)
        {
            _logger.LogWarning(
                "ERR_COMPONENT_NOT_FOUND: Cannot override state — component {ComponentId} not found.",
                command.ComponentId);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var state = await _stateRepository.GetByComponentIdAsync(command.ComponentId, ct)
                    ?? new ComponentState(command.ComponentId);

        var previousStatus = state.Status;
        state.UpdateStatus(command.NewStatus, now);
        await _stateRepository.UpsertAsync(state, ct);
        _stateCache.SetState(state);

        _logger.LogInformation(
            "Component state overridden: Component={ComponentId}, {Previous}->{New}, Operator={Operator}",
            command.ComponentId, previousStatus, command.NewStatus, command.OperatorName);

        // Dispatch domain event so downstream handlers (e.g., AlertEvaluationService) are notified
        var overriddenEvent = new ComponentStateOverridden(
            ComponentId: command.ComponentId,
            SystemId: command.SystemId,
            PreviousStatus: previousStatus,
            NewStatus: command.NewStatus,
            OperatorName: command.OperatorName,
            Reason: command.Reason,
            OccurredAt: now);

        await _eventDispatcher.DispatchAsync(overriddenEvent, ct);

        // Audit log
        await _auditLogger.LogOperatorActionAsync(
            systemId: command.SystemId,
            componentId: command.ComponentId,
            actionType: "ComponentStateOverridden",
            operatorName: command.OperatorName,
            reason: command.Reason,
            occurredAt: now,
            ct: ct);

        // SignalR push — notify UI via ComponentStatusChanged shape
        var statusChangedEvent = new ComponentStatusChanged(
            ComponentId: command.ComponentId,
            SystemId: command.SystemId,
            PreviousStatus: previousStatus,
            NewStatus: command.NewStatus,
            OccurredAt: now);

        await _realtimeNotification.NotifyComponentStatusChangedAsync(statusChangedEvent, ct);
    }
}
