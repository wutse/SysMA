using BrokerageMonitor.Domain.ReadModels;

namespace BrokerageMonitor.Domain.Repositories;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// 30-day data retention (FR-024).
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
