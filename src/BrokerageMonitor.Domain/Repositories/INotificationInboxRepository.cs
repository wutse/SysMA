using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Domain.Repositories;

public interface INotificationInboxRepository
{
    Task AddAsync(NotificationInboxItem item, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationInboxItem>> GetAllAsync(CancellationToken ct = default);
    Task MarkAsReadAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// 30-day data retention (FR-024).
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
