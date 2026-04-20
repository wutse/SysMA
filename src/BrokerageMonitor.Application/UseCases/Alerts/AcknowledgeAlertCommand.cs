namespace BrokerageMonitor.Application.UseCases.Alerts;

/// <summary>
/// Command to acknowledge all active alerts for a given system (FR-018, FR-019).
/// Acknowledgement clears the system-wide GlobalFlag and records the operator (BI-009).
/// </summary>
public sealed record AcknowledgeAlertCommand(
    string SystemId,
    string OperatorName);
