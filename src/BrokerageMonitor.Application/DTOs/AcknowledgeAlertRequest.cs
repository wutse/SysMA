namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Payload sent from the Blazor client when acknowledging all active alerts for a system.
/// Mapped to <see cref="BrokerageMonitor.Application.UseCases.Alerts.AcknowledgeAlertCommand"/> by MonitorHub.
/// FR-018, FR-019.
/// </summary>
public sealed record AcknowledgeAlertRequest(string SystemId, string OperatorName);
