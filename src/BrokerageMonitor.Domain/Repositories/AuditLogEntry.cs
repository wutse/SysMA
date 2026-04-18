namespace BrokerageMonitor.Domain.Repositories;

/// <summary>
/// Immutable audit log entry. Written once, never modified.
/// </summary>
public sealed record AuditLogEntry(
    Guid AuditId,
    string SystemId,
    string? ComponentId,
    string ActionType,
    string OperatorName,
    string? Reason,
    DateTimeOffset OccurredAt);
