using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Returns all active <see cref="MonitoredComponent"/> aggregates ordered by system then name.
/// Used by the Health Definition editor to populate the component picker.
/// </summary>
public sealed class GetActiveComponentsQueryHandler
{
  private readonly IMonitoredComponentRepository _componentRepo;

  public GetActiveComponentsQueryHandler(IMonitoredComponentRepository componentRepo)
      => _componentRepo = componentRepo;

  public async Task<IReadOnlyList<MonitoredComponent>> HandleAsync(CancellationToken ct = default)
  {
    var components = await _componentRepo.GetAllActiveAsync(ct).ConfigureAwait(false);
    return components.OrderBy(c => c.SystemId).ThenBy(c => c.Name).ToList();
  }
}
