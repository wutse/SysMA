using BrokerageMonitor.Domain.ReadModels;
using BrokerageMonitor.Domain.Repositories;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _factory;

    public AuditLogRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO AuditLogs
                (LogId, SystemId, ComponentId, OperatorName, ActionType,
                 PreviousStatus, NewStatus, Reason, OccurredAt)
            VALUES
                (@LogId, @SystemId, @ComponentId, @OperatorName, @ActionType,
                 @PreviousStatus, @NewStatus, @Reason, @OccurredAt);
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            LogId    = entry.AuditId.ToString(),
            entry.SystemId,
            entry.ComponentId,
            entry.OperatorName,
            entry.ActionType,
            PreviousStatus = (string?)null,
            NewStatus      = (string?)null,
            Reason         = entry.Reason ?? string.Empty,
            OccurredAt     = entry.OccurredAt.ToString("O")
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT * FROM AuditLogs
            WHERE (@SystemId IS NULL OR SystemId = @SystemId)
              AND OccurredAt >= @From
              AND OccurredAt <= @To
            ORDER BY OccurredAt DESC;
            """;
        var rows = await conn.QueryAsync<AuditLogRow>(new CommandDefinition(sql, new
        {
            SystemId = systemId,
            From     = from.ToString("O"),
            To       = to.ToString("O")
        }, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "DELETE FROM AuditLogs WHERE OccurredAt < @Cutoff";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { Cutoff = cutoff.ToString("O") }, cancellationToken: ct));
    }

    private static AuditLogEntry MapToDomain(AuditLogRow row) => new(
        Guid.Parse(row.LogId),
        row.SystemId,
        row.ComponentId,
        row.ActionType,
        row.OperatorName,
        row.Reason,
        DateTimeOffset.Parse(row.OccurredAt));

    private sealed record AuditLogRow(
        string  LogId,
        string  SystemId,
        string? ComponentId,
        string  OperatorName,
        string  ActionType,
        string? PreviousStatus,
        string? NewStatus,
        string? Reason,
        string  OccurredAt);
}
