using BrokerageMonitor.Application.DTOs;
using BrokerageMonitor.Application.UseCases.Alerts;
using BrokerageMonitor.Application.UseCases.Dashboard;
using BrokerageMonitor.Application.UseCases.Maintenance;
using BrokerageMonitor.Application.UseCases.StateOverride;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// SignalR Hub that bridges the Blazor client and the server-side monitoring domain.
///
/// Server → Client push methods (called via <see cref="IHubContext{MonitorHub}"/>):
/// <list type="bullet">
///   <item><c>OnComponentStatusChanged</c> — <see cref="ComponentStatusDto"/></item>
///   <item><c>OnAlertTriggered</c>          — <see cref="AlertDto"/></item>
///   <item><c>OnAlertAcknowledged</c>       — <c>{ SystemId, AcknowledgedBy }</c></item>
///   <item><c>OnMaintenanceModeChanged</c>  — <c>{ SystemId, IsActive }</c></item>
///   <item><c>OnHealthNotificationReceived</c> — <see cref="NotificationInboxItemDto"/></item>
///   <item><c>OnDailyExecutionUpdated</c>   — <c>{ ExecutionId }</c></item>
/// </list>
///
/// Client → Server invoke methods exposed by this class (all return <see cref="Result{T}"/>):
/// <list type="bullet">
///   <item><see cref="SubscribeDashboard"/>     — joins Dashboard group, returns initial summary.</item>
///   <item><see cref="AcknowledgeAlert"/>       — delegates to <see cref="AcknowledgeAlertHandler"/>.</item>
///   <item><see cref="OverrideComponentState"/> — delegates to <see cref="OverrideComponentStateHandler"/>.</item>
///   <item><see cref="ToggleMaintenanceMode"/>  — delegates to <see cref="ToggleMaintenanceModeHandler"/>.</item>
/// </list>
///
/// Design Doc §4.1. US-035, EP-007.
/// </summary>
public sealed class MonitorHub : Hub
{
    private const string DashboardGroup = "Dashboard";

    private readonly GetDashboardQueryHandler _dashboardHandler;
    private readonly AcknowledgeAlertHandler _acknowledgeHandler;
    private readonly OverrideComponentStateHandler _overrideHandler;
    private readonly ToggleMaintenanceModeHandler _maintenanceHandler;
    private readonly ILogger<MonitorHub> _logger;

    public MonitorHub(
        GetDashboardQueryHandler dashboardHandler,
        AcknowledgeAlertHandler acknowledgeHandler,
        OverrideComponentStateHandler overrideHandler,
        ToggleMaintenanceModeHandler maintenanceHandler,
        ILogger<MonitorHub> logger)
    {
        _dashboardHandler = dashboardHandler;
        _acknowledgeHandler = acknowledgeHandler;
        _overrideHandler = overrideHandler;
        _maintenanceHandler = maintenanceHandler;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Client → Server Invocations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Subscribes the calling connection to the Dashboard push group and returns the initial
    /// snapshot of all monitored systems with their current component statuses.
    /// </summary>
    public async Task<Result<IReadOnlyList<SystemSummaryDto>>> SubscribeDashboard(
        CancellationToken ct = default)
    {
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup, ct);
            var summary = await _dashboardHandler.HandleAsync(new GetDashboardQuery(), ct);
            return Result<IReadOnlyList<SystemSummaryDto>>.Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SubscribeDashboard failed for connection {ConnectionId}.",
                Context.ConnectionId);
            return Result<IReadOnlyList<SystemSummaryDto>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Acknowledges all active alerts for the specified system (FR-018, FR-019).
    /// </summary>
    public async Task<Result<bool>> AcknowledgeAlert(
        AcknowledgeAlertRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _acknowledgeHandler.HandleAsync(
                new AcknowledgeAlertCommand(request.SystemId, request.OperatorName), ct);
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AcknowledgeAlert failed for system {SystemId}.", request.SystemId);
            return Result<bool>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Manually overrides the operational state of a component (FR-005/006/007).
    /// </summary>
    public async Task<Result<bool>> OverrideComponentState(
        OverrideComponentStateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _overrideHandler.HandleAsync(
                new OverrideComponentStateCommand(
                    request.SystemId,
                    request.ComponentId,
                    request.NewStatus,
                    request.OperatorName,
                    request.Reason), ct);
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OverrideComponentState failed for component {ComponentId}.",
                request.ComponentId);
            return Result<bool>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Toggles maintenance mode on or off for the specified system (FR-032).
    /// </summary>
    public async Task<Result<bool>> ToggleMaintenanceMode(
        ToggleMaintenanceModeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _maintenanceHandler.HandleAsync(
                new ToggleMaintenanceModeCommand(
                    request.SystemId,
                    request.Activate,
                    request.OperatorName), ct);
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ToggleMaintenanceMode failed for system {SystemId}.", request.SystemId);
            return Result<bool>.Fail(ex.Message);
        }
    }
}
