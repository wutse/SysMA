using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface IMonitoredSystemRepository
{
    Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default);
    Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default);
    Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default);
    Task SetMaintenanceModeAsync(string systemId, bool active, CancellationToken ct = default);
}
