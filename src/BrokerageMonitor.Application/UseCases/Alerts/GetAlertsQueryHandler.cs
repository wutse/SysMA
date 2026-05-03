using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Alerts;

/// <summary>
/// Handles <see cref="GetAlertsQuery"/> — returns unacknowledged alerts or alert history.
/// </summary>
public sealed class GetAlertsQueryHandler
{
  private readonly IAlertRecordRepository _alertRepo;
  private readonly TimeProvider _timeProvider;

  public GetAlertsQueryHandler(IAlertRecordRepository alertRepo, TimeProvider timeProvider)
  {
    _alertRepo = alertRepo;
    _timeProvider = timeProvider;
  }

  public async Task<IReadOnlyList<AlertRecord>> HandleAsync(
      GetAlertsQuery query,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    if (query.UnacknowledgedOnly)
      return await _alertRepo.GetUnacknowledgedAsync(ct).ConfigureAwait(false);

    // N4: single clock read per operation — prevents two distinct timestamps
    // when both From and To are null, and preserves FakeTimeProvider testability.
    var now = _timeProvider.GetUtcNow();
    var from = query.From ?? now.AddDays(-7);
    var to = query.To ?? now;
    return await _alertRepo.GetHistoryAsync(null, from, to, ct).ConfigureAwait(false);
  }
}
