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
    Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default);
    Task DeleteAsync(Guid definitionId, CancellationToken ct = default);
}
