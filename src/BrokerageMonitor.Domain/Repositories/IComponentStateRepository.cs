using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface IComponentStateRepository
{
    Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default);

    /// <summary>
    /// Batch fetch for health evaluation (FR-045).
    /// </summary>
    Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> componentIds, CancellationToken ct = default);

    Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Upsert state for station restart recovery (FR-033).
    /// </summary>
    Task UpsertAsync(ComponentState state, CancellationToken ct = default);
}
