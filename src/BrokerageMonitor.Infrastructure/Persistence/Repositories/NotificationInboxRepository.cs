using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class NotificationInboxRepository : INotificationInboxRepository
{
    private readonly IDbConnectionFactory _factory;

    public NotificationInboxRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(NotificationInboxItem item, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO NotificationInbox
                (InboxItemId, DefinitionId, ExecutionId, Title, Body, NotificationType, SentAt, IsRead)
            VALUES
                (@InboxItemId, @DefinitionId, @ExecutionId, @Title, @Body, @NotificationType, @SentAt, 0);
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            InboxItemId      = item.InboxItemId.ToString(),
            DefinitionId     = item.DefinitionId.ToString(),
            ExecutionId      = item.ExecutionId?.ToString(),
            item.Title,
            item.Body,
            NotificationType = item.NotificationType.ToString(),
            SentAt           = item.SentAt.ToString("O")
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<NotificationInboxItem>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM NotificationInbox ORDER BY SentAt DESC";
        var rows = await conn.QueryAsync<InboxItemRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task MarkAsReadAsync(Guid itemId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "UPDATE NotificationInbox SET IsRead = 1 WHERE InboxItemId = @ItemId";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { ItemId = itemId.ToString() }, cancellationToken: ct));
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "DELETE FROM NotificationInbox WHERE SentAt < @Cutoff";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { Cutoff = cutoff.ToString("O") }, cancellationToken: ct));
    }

    private static NotificationInboxItem MapToDomain(InboxItemRow row)
    {
        var item = new NotificationInboxItem(
            Guid.Parse(row.InboxItemId),
            Guid.Parse(row.DefinitionId),
            row.ExecutionId is null ? null : Guid.Parse(row.ExecutionId),
            row.Title,
            row.Body,
            Enum.Parse<NotificationType>(row.NotificationType),
            DateTimeOffset.Parse(row.SentAt));

        if (row.IsRead == 1)
            item.MarkAsRead();

        return item;
    }

    private sealed record InboxItemRow(
        string  InboxItemId,
        string  DefinitionId,
        string? ExecutionId,
        string  Title,
        string  Body,
        string  NotificationType,
        string  SentAt,
        int     IsRead);
}
