using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.DTOs;

/// <summary>
/// DTO representing a triggered alert record for SignalR push and UI display (FR-010, FR-017).
/// </summary>
public sealed record AlertDto(
    Guid AlertId,
    string SystemId,
    string SystemName,
    string ComponentId,
    string ComponentName,
    ComponentType ComponentType,
    ComponentStatus AlertStatus,
    DateTimeOffset OccurredAt,
    bool IsAcknowledged);
