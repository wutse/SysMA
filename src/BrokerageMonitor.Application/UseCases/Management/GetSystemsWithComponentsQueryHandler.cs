using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Returns all active <see cref="MonitoredSystem"/> aggregates together with their components,
/// ordered by <see cref="MonitoredSystem.SystemId"/>. Used by the System Management page.
/// </summary>
public sealed class GetSystemsWithComponentsQueryHandler
{
  private readonly IMonitoredSystemRepository _systemRepo;
  private readonly IMonitoredComponentRepository _componentRepo;

  public GetSystemsWithComponentsQueryHandler(
      IMonitoredSystemRepository systemRepo,
      IMonitoredComponentRepository componentRepo)
  {
    _systemRepo = systemRepo;
    _componentRepo = componentRepo;
  }

  public async Task<(IReadOnlyList<MonitoredSystem> Systems,
                     IReadOnlyDictionary<string, IReadOnlyList<MonitoredComponent>> ComponentsBySystem)>
      HandleAsync(CancellationToken ct = default)
  {
    // Two queries instead of N+1: load all systems, then load all components
    // and group in-memory — O(M) instead of O(N) round-trips.
    var allSystems = (await _systemRepo.GetAllActiveAsync(ct).ConfigureAwait(false))
        .OrderBy(s => s.SystemId)
        .ToList();

    var allComponents = await _componentRepo.GetAllActiveAsync(ct).ConfigureAwait(false);

    var componentsBySystem = allSystems.ToDictionary(
        s => s.SystemId,
        s => (IReadOnlyList<MonitoredComponent>)allComponents
            .Where(c => c.SystemId == s.SystemId)
            .OrderBy(c => c.Name)
            .ToList(),
        StringComparer.Ordinal);

    return (allSystems, componentsBySystem);
  }
}
