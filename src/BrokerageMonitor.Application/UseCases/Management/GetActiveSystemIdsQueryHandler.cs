using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Returns the ordered list of active system IDs for use in filter drop-downs.
/// Avoids injecting <see cref="IMonitoredSystemRepository"/> directly into the presentation layer.
/// </summary>
public sealed class GetActiveSystemIdsQueryHandler
{
  private readonly IMonitoredSystemRepository _systemRepo;

  public GetActiveSystemIdsQueryHandler(IMonitoredSystemRepository systemRepo)
      => _systemRepo = systemRepo;

  public async Task<IReadOnlyList<string>> HandleAsync(CancellationToken ct = default)
  {
    var systems = await _systemRepo.GetAllActiveAsync(ct).ConfigureAwait(false);
    return systems.Select(s => s.SystemId).OrderBy(id => id).ToList();
  }
}
