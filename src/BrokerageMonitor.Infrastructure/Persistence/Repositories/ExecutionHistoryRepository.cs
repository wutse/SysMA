using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class ExecutionHistoryRepository : IExecutionHistoryRepository
{
    private readonly IDbConnectionFactory _factory;

    public ExecutionHistoryRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(ExecutionHistoryEntry entry, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO ExecutionHistory
                (HistoryId, SystemId, ComponentId, ComponentName, ComponentType,
                 ResultStatus, StartedAt, EndedAt, Message)
            VALUES
                (@HistoryId, @SystemId, @ComponentId, @ComponentName, @ComponentType,
                 @ResultStatus, @StartedAt, @EndedAt, @Message);
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            HistoryId     = entry.HistoryId.ToString(),
            entry.SystemId,
            entry.ComponentId,
            entry.ComponentName,
            ComponentType = entry.ComponentType.ToString(),
            ResultStatus  = entry.ResultStatus.ToString(),
            StartedAt     = entry.StartedAt.ToString("O"),
            EndedAt       = entry.EndedAt?.ToString("O"),
            entry.Message
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ExecutionHistoryEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT * FROM ExecutionHistory
            WHERE (@SystemId IS NULL OR SystemId = @SystemId)
              AND StartedAt >= @From
              AND StartedAt <= @To
            ORDER BY StartedAt DESC;
            """;
        var rows = await conn.QueryAsync<ExecutionHistoryRow>(new CommandDefinition(sql, new
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
        const string sql = "DELETE FROM ExecutionHistory WHERE StartedAt < @Cutoff";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { Cutoff = cutoff.ToString("O") }, cancellationToken: ct));
    }

    private static ExecutionHistoryEntry MapToDomain(ExecutionHistoryRow row) => new(
        Guid.Parse(row.HistoryId),
        row.SystemId,
        row.ComponentId,
        row.ComponentName,
        Enum.Parse<ComponentType>(row.ComponentType),
        Enum.Parse<ComponentStatus>(row.ResultStatus),
        DateTimeOffset.Parse(row.StartedAt),
        row.EndedAt is null ? null : DateTimeOffset.Parse(row.EndedAt),
        row.Message);

    private sealed record ExecutionHistoryRow(
        string  HistoryId,
        string  SystemId,
        string  ComponentId,
        string  ComponentName,
        string  ComponentType,
        string  ResultStatus,
        string  StartedAt,
        string? EndedAt,
        string? Message);
}
