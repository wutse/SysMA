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

        var from = query.From ?? _timeProvider.GetUtcNow().AddDays(-7);
        var to = query.To ?? _timeProvider.GetUtcNow();
        return await _alertRepo.GetHistoryAsync(null, from, to, ct).ConfigureAwait(false);
    }
}
