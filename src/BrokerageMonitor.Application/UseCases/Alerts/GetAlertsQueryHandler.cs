using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Alerts;

/// <summary>
/// Handles <see cref="GetAlertsQuery"/> — returns unacknowledged alerts or alert history.
/// </summary>
public sealed class GetAlertsQueryHandler
{
  private readonly IAlertRecordRepository _alertRepo;

  public GetAlertsQueryHandler(IAlertRecordRepository alertRepo)
      => _alertRepo = alertRepo;

  public async Task<IReadOnlyList<AlertRecord>> HandleAsync(
      GetAlertsQuery query,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    if (query.UnacknowledgedOnly)
      return await _alertRepo.GetUnacknowledgedAsync(ct).ConfigureAwait(false);

    var from = query.From ?? DateTimeOffset.UtcNow.AddDays(-7);
    var to = query.To ?? DateTimeOffset.UtcNow;
    return await _alertRepo.GetHistoryAsync(null, from, to, ct).ConfigureAwait(false);
  }
}
