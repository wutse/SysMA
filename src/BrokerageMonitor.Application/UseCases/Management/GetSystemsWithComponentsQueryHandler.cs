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
    var allSystems = (await _systemRepo.GetAllActiveAsync(ct).ConfigureAwait(false))
        .OrderBy(s => s.SystemId)
        .ToList();

    var componentsBySystem = new Dictionary<string, IReadOnlyList<MonitoredComponent>>(
        StringComparer.Ordinal);

    foreach (var sys in allSystems)
    {
      var comps = await _componentRepo.GetBySystemIdAsync(sys.SystemId, ct).ConfigureAwait(false);
      componentsBySystem[sys.SystemId] = comps.OrderBy(c => c.Name).ToList();
    }

    return (allSystems, componentsBySystem);
  }
}
