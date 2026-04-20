using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Aggregated view of a monitored system for the dashboard.
/// RolledUpStatus is the worst-case of all active component statuses (FR-001).
/// HasUnacknowledgedAlert drives the alert badge on the system card (FR-011).
/// </summary>
public sealed record SystemSummaryDto(
    string SystemId,
    string Name,
    ComponentStatus RolledUpStatus,
    bool IsMaintenanceActive,
    bool HasUnacknowledgedAlert,
    IReadOnlyList<ComponentStatusDto> Components);
