namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Payload sent from the Blazor client to toggle maintenance mode on or off for a system.
/// Mapped to <see cref="BrokerageMonitor.Application.UseCases.Maintenance.ToggleMaintenanceModeCommand"/> by MonitorHub.
/// FR-032.
/// </summary>
public sealed record ToggleMaintenanceModeRequest(
    string SystemId,
    bool Activate,
    string OperatorName);
