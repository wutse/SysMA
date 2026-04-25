using BrokerageMonitor.Domain.ReadModels;

namespace BrokerageMonitor.Domain.Repositories;

public interface IExecutionHistoryRepository
{
    Task AddAsync(ExecutionHistoryEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionHistoryEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// 30-day data retention (FR-024).
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
