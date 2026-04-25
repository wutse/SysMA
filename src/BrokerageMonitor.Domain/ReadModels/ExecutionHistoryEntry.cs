using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.ReadModels;

/// <summary>
/// Immutable snapshot record written at history entry creation time.
/// ComponentName and ComponentType are snapshots — stable after renames/deletions.
/// </summary>
public sealed record ExecutionHistoryEntry(
    Guid HistoryId,
    string SystemId,
    string ComponentId,
    string ComponentName,
    ComponentType ComponentType,
    ComponentStatus ResultStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Message);
