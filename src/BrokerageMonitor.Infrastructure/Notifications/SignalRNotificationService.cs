using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// Implements <see cref="IRealtimeNotificationService"/> using ASP.NET Core SignalR.
/// Pushes domain events to all connected Blazor clients via <see cref="MonitorHub"/>.
/// Push failures are logged as WARNING and swallowed — they must not impact domain logic.
/// US-036, EP-007.
/// </summary>
public sealed class SignalRNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<MonitorHub> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<MonitorHub> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task NotifyComponentStatusChangedAsync(
        ComponentStatusChanged evt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "OnComponentStatusChanged",
                new
                {
                    evt.ComponentId,
                    evt.SystemId,
                    evt.PreviousStatus,
                    evt.NewStatus,
                    evt.OccurredAt
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push OnComponentStatusChanged failed for component {ComponentId}.",
                evt.ComponentId);
        }
    }

    /// <inheritdoc/>
    public async Task NotifyAlertTriggeredAsync(
        string systemId,
        string componentId,
        CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "OnAlertTriggered",
                new { SystemId = systemId, ComponentId = componentId },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push OnAlertTriggered failed for system {SystemId}.", systemId);
        }
    }

    /// <inheritdoc/>
    public async Task NotifyAlertAcknowledgedAsync(
        string systemId,
        CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "OnAlertAcknowledged",
                new { SystemId = systemId },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push OnAlertAcknowledged failed for system {SystemId}.", systemId);
        }
    }

    /// <inheritdoc/>
    public async Task NotifyMaintenanceModeChangedAsync(
        string systemId,
        bool isActive,
        CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "OnMaintenanceModeChanged",
                new { SystemId = systemId, IsActive = isActive },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push OnMaintenanceModeChanged failed for system {SystemId}.", systemId);
        }
    }

    /// <inheritdoc/>
    public async Task NotifyDailyExecutionUpdatedAsync(
        Guid executionId,
        CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                "OnDailyExecutionUpdated",
                new { ExecutionId = executionId },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push OnDailyExecutionUpdated failed for execution {ExecutionId}.",
                executionId);
        }
    }
}
