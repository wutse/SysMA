using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface IAlertRecordRepository
{
    Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default);
    Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default);

    /// <summary>
    /// Returns the set of SystemIds that have at least one unacknowledged alert.
    /// Use this instead of per-system <see cref="HasUnacknowledgedAlertAsync"/> on the
    /// dashboard to avoid 2N+1 queries.
    /// </summary>
    Task<IReadOnlySet<string>> GetSystemsWithUnacknowledgedAlertAsync(CancellationToken ct = default);

    Task AddAsync(AlertRecord alert, CancellationToken ct = default);
    /// <summary>
    /// Bulk-acknowledges all active alerts for the given system.
    /// Implementations MUST filter with <c>WHERE AcknowledgedAt IS NULL</c> so that
    /// already-acknowledged records are never mutated.
    /// The <paramref name="acknowledgedAt"/> timestamp is supplied by the caller so that the
    /// Application layer's <see cref="TimeProvider"/> drives all time values.
    /// </summary>
    Task AcknowledgeBySystemAsync(string systemId, string operatorName, DateTimeOffset acknowledgedAt, CancellationToken ct = default);
    Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
