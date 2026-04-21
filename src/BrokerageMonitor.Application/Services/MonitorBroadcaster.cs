using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Thread-safe in-process event broadcaster for Blazor Server real-time updates.
/// Registered as Singleton. Components subscribe on init and unsubscribe on dispose.
/// </summary>
public sealed class MonitorBroadcaster : IMonitorBroadcaster
{
    private readonly ILogger<MonitorBroadcaster> _logger;

    public MonitorBroadcaster(ILogger<MonitorBroadcaster> logger)
        => _logger = logger;

    public event Action<string, string, ComponentStatus>? ComponentStatusUpdated;
    public event Action<string, string>? AlertTriggered;
    public event Action<string>? AlertAcknowledged;
    public event Action<string, bool>? MaintenanceModeChanged;
    public event Action<Guid, Guid, NotificationType>? HealthNotificationReceived;

    public void PublishComponentStatusChanged(string componentId, string systemId, ComponentStatus newStatus)
    {
        try { ComponentStatusUpdated?.Invoke(componentId, systemId, newStatus); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error in ComponentStatusUpdated handler."); }
    }

    public void PublishAlertTriggered(string systemId, string componentId)
    {
        try { AlertTriggered?.Invoke(systemId, componentId); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error in AlertTriggered handler."); }
    }

    public void PublishAlertAcknowledged(string systemId)
    {
        try { AlertAcknowledged?.Invoke(systemId); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error in AlertAcknowledged handler."); }
    }

    public void PublishMaintenanceModeChanged(string systemId, bool isActive)
    {
        try { MaintenanceModeChanged?.Invoke(systemId, isActive); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error in MaintenanceModeChanged handler."); }
    }

    public void PublishHealthNotificationReceived(Guid definitionId, Guid executionId, NotificationType notificationType)
    {
        try { HealthNotificationReceived?.Invoke(definitionId, executionId, notificationType); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error in HealthNotificationReceived handler."); }
    }
}
