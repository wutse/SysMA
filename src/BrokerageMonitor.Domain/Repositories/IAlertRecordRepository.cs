using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface IAlertRecordRepository
{
    Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default);
    Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default);
    Task AddAsync(AlertRecord alert, CancellationToken ct = default);
    Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default);
    Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
