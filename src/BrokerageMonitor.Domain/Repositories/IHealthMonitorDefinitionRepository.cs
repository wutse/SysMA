using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

/// <summary>
/// Replaces IAggregateHealthRuleRepository.
/// </summary>
public interface IHealthMonitorDefinitionRepository
{
    Task<HealthMonitorDefinition?> GetByIdAsync(Guid definitionId, CancellationToken ct = default);
    Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default);

    /// <summary>
    /// Returns all active definitions that include <paramref name="componentId"/> in their
    /// <c>WatchedComponents</c> collection. Use this instead of <see cref="GetAllActiveAsync"/>
    /// when filtering per-component to avoid a full table scan on every heartbeat event.
    /// </summary>
    Task<IReadOnlyList<HealthMonitorDefinition>> GetByWatchedComponentAsync(string componentId, CancellationToken ct = default);

    Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default);
    Task DeleteAsync(Guid definitionId, CancellationToken ct = default);
}
