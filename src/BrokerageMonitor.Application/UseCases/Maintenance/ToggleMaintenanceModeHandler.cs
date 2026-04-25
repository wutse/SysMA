using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Maintenance;

/// <summary>
/// Handles <see cref="ToggleMaintenanceModeCommand"/> to activate or deactivate
/// maintenance mode for a monitored system (US-033).
///
/// Business rules:
/// <list type="bullet">
///   <item>OperatorName must not be empty (BI-009).</item>
///   <item>On Activate: sets <c>IsMaintenanceActive</c> on the system aggregate;
///     sets all component states to <see cref="ComponentStatus.Maintenance"/>.</item>
///   <item>On Deactivate: clears maintenance flag; resets all component states to
///     <see cref="ComponentStatus.Unknown"/>.</item>
///   <item>Raises <see cref="MaintenanceModeToggled"/> domain event for subscribers.</item>
///   <item>Writes an operator-action audit log entry.</item>
///   <item>Pushes <c>OnMaintenanceModeChanged</c> SignalR notification (FR-032).</item>
/// </list>
/// </summary>
public sealed class ToggleMaintenanceModeHandler
{
    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IComponentStateRepository _stateRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IAuditLogger _auditLogger;
    private readonly IRealtimeNotificationService _realtimeNotification;
    private readonly ILogger<ToggleMaintenanceModeHandler> _logger;

    public ToggleMaintenanceModeHandler(
        IMonitoredSystemRepository systemRepository,
        IMonitoredComponentRepository componentRepository,
        IComponentStateRepository stateRepository,
        IComponentStateCache stateCache,
        IAuditLogger auditLogger,
        IRealtimeNotificationService realtimeNotification,
        ILogger<ToggleMaintenanceModeHandler> logger)
    {
        _systemRepository = systemRepository;
        _componentRepository = componentRepository;
        _stateRepository = stateRepository;
        _stateCache = stateCache;
        _auditLogger = auditLogger;
        _realtimeNotification = realtimeNotification;
        _logger = logger;
    }

    /// <summary>Executes the toggle-maintenance-mode workflow.</summary>
    public async Task HandleAsync(ToggleMaintenanceModeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OperatorName))
            throw new ArgumentException(
                "OperatorName is required to toggle maintenance mode (BI-009).",
                nameof(command));

        if (string.IsNullOrWhiteSpace(command.SystemId))
            throw new ArgumentException(
                "SystemId cannot be empty.",
                nameof(command));

        var system = await _systemRepository.GetByIdAsync(command.SystemId, ct);
        if (system is null)
        {
            _logger.LogWarning(
                "ERR_SYSTEM_NOT_FOUND: Cannot toggle maintenance — system {SystemId} not found.",
                command.SystemId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var targetStatus = command.Activate
            ? ComponentStatus.Maintenance
            : ComponentStatus.Unknown;

        // Toggle system aggregate maintenance flag
        if (command.Activate)
            system.ActivateMaintenance(command.OperatorName);
        else
            system.DeactivateMaintenance(command.OperatorName);

        await _systemRepository.UpsertAsync(system, ct);

        // Update all component states for this system
        var components = await _componentRepository.GetBySystemIdAsync(command.SystemId, ct);

        foreach (var component in components)
        {
            var state = await _stateRepository.GetByComponentIdAsync(component.ComponentId, ct)
                        ?? new ComponentState(component.ComponentId);

            state.UpdateStatus(targetStatus, now);
            await _stateRepository.UpsertAsync(state, ct);
            _stateCache.SetState(state);
        }

        _logger.LogInformation(
            "Maintenance mode {Action}: System={SystemId}, Components={Count}, Operator={Operator}",
            command.Activate ? "activated" : "deactivated",
            command.SystemId,
            components.Count,
            command.OperatorName);

        // Audit log
        await _auditLogger.LogOperatorActionAsync(
            systemId: command.SystemId,
            componentId: null,
            actionType: command.Activate ? "MaintenanceActivated" : "MaintenanceDeactivated",
            operatorName: command.OperatorName,
            reason: null,
            occurredAt: now,
            ct: ct);

        // SignalR push (FR-032)
        await _realtimeNotification.NotifyMaintenanceModeChangedAsync(command.SystemId, command.Activate, ct);
    }
}
