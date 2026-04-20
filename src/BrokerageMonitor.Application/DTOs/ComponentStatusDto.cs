using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// Snapshot of a single component's runtime status for dashboard display.
/// FR-001, FR-039.
/// </summary>
public sealed record ComponentStatusDto(
    string ComponentId,
    string Name,
    ComponentType ComponentType,
    ComponentStatus Status,
    DateTimeOffset? LastHeartbeatAt,
    IReadOnlyList<SubIndicator> SubIndicators);
