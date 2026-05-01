namespace BrokerageMonitor.Application.UseCases.Alerts;

/// <summary>
/// Query to retrieve alert records.
/// When <see cref="UnacknowledgedOnly"/> is <c>true</c>, returns all unacknowledged alerts.
/// When <c>false</c>, returns history between <see cref="From"/> and <see cref="To"/>.
/// </summary>
public sealed record GetAlertsQuery(
    bool UnacknowledgedOnly,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
