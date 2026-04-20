namespace BrokerageMonitor.Application.UseCases.Maintenance;

/// <summary>
/// Command to toggle maintenance mode on or off for a monitored system.
/// </summary>
/// <param name="SystemId">The target system identifier.</param>
/// <param name="Activate"><c>true</c> to activate maintenance; <c>false</c> to deactivate.</param>
/// <param name="OperatorName">Operator performing the action (BI-009).</param>
public sealed record ToggleMaintenanceModeCommand(
    string SystemId,
    bool Activate,
    string OperatorName);
