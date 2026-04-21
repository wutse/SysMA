using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Payload sent from the Blazor client to manually override a component's operational state.
/// Mapped to <see cref="BrokerageMonitor.Application.UseCases.StateOverride.OverrideComponentStateCommand"/> by MonitorHub.
/// FR-005/006/007, BI-001/009.
/// </summary>
public sealed record OverrideComponentStateRequest(
    string SystemId,
    string ComponentId,
    ComponentStatus NewStatus,
    string OperatorName,
    string Reason);
