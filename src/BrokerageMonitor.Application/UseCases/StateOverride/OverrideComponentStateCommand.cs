using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.UseCases.StateOverride;

/// <summary>
/// Command to manually override a component's operational state (FR-005/006/007).
/// </summary>
/// <param name="SystemId">The system owning the component.</param>
/// <param name="ComponentId">The target component identifier.</param>
/// <param name="NewStatus">The status to force onto the component.</param>
/// <param name="OperatorName">Operator performing the override (BI-001, BI-009).</param>
/// <param name="Reason">Mandatory reason for the override (BI-001).</param>
public sealed record OverrideComponentStateCommand(
    string SystemId,
    string ComponentId,
    ComponentStatus NewStatus,
    string OperatorName,
    string Reason);
