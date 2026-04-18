using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface IMonitoredComponentRepository
{
    Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default);
    Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default);
    Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default);
    Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default);
}
