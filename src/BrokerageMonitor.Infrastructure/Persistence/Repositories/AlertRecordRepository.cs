using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class AlertRecordRepository : IAlertRecordRepository
{
    private readonly IDbConnectionFactory _factory;

    public AlertRecordRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM AlertRecords WHERE IsGlobalFlagActive = 1";
        var rows = await conn.QueryAsync<AlertRecordRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT COUNT(1) FROM AlertRecords
            WHERE SystemId = @SystemId AND IsGlobalFlagActive = 1
            """;
        var count = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { SystemId = systemId }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<IReadOnlySet<string>> GetSystemsWithUnacknowledgedAlertAsync(
        CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT DISTINCT SystemId FROM AlertRecords
            WHERE IsGlobalFlagActive = 1
            """;
        var systemIds = await conn.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: ct));
        return systemIds.ToHashSet();
    }

    public async Task AddAsync(AlertRecord alert, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO AlertRecords
                (AlertId, SystemId, ComponentId, AlertStatus, OccurredAt, IsGlobalFlagActive, AcknowledgedBy, AcknowledgedAt)
            VALUES
                (@AlertId, @SystemId, @ComponentId, @AlertStatus, @OccurredAt, @IsGlobalFlagActive, @AcknowledgedBy, @AcknowledgedAt);
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            AlertId            = alert.AlertId.ToString(),
            alert.SystemId,
            alert.ComponentId,
            AlertStatus        = alert.AlertStatus.ToString(),
            OccurredAt         = alert.OccurredAt.ToString("O"),
            IsGlobalFlagActive = alert.IsGlobalFlagActive ? 1 : 0,
            alert.AcknowledgedBy,
            AcknowledgedAt     = alert.AcknowledgedAt?.ToString("O")
        }, cancellationToken: ct));
    }

    public async Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            UPDATE AlertRecords
            SET IsGlobalFlagActive = 0,
                AcknowledgedBy     = @OperatorName,
                AcknowledgedAt     = @Now
            WHERE SystemId = @SystemId AND IsGlobalFlagActive = 1;
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            OperatorName = operatorName,
            Now          = DateTimeOffset.UtcNow.ToString("O"),
            SystemId     = systemId
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT * FROM AlertRecords
            WHERE (@SystemId IS NULL OR SystemId = @SystemId)
              AND OccurredAt >= @From
              AND OccurredAt <= @To
            ORDER BY OccurredAt DESC;
            """;

        var rows = await conn.QueryAsync<AlertRecordRow>(new CommandDefinition(sql, new
        {
            SystemId = systemId,
            From     = from.ToString("O"),
            To       = to.ToString("O")
        }, cancellationToken: ct));

        return rows.Select(MapToDomain).ToList();
    }

    private static AlertRecord MapToDomain(AlertRecordRow row)
    {
        var alert = new AlertRecord(
            Guid.Parse(row.AlertId),
            row.SystemId,
            row.ComponentId,
            Enum.Parse<ComponentStatus>(row.AlertStatus),
            DateTimeOffset.Parse(row.OccurredAt),
            row.IsGlobalFlagActive == 1);

        if (row.AcknowledgedBy is not null)
            alert.Acknowledge(row.AcknowledgedBy, DateTimeOffset.Parse(row.AcknowledgedAt!));

        return alert;
    }

    private sealed record AlertRecordRow(
        string  AlertId,
        string  SystemId,
        string  ComponentId,
        string  AlertStatus,
        string  OccurredAt,
        long    IsGlobalFlagActive,
        string? AcknowledgedBy,
        string? AcknowledgedAt);
}
