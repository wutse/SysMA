using System.Text.Json;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class ComponentStateRepository : IComponentStateRepository
{
    private readonly IDbConnectionFactory _factory;

    public ComponentStateRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM ComponentStates WHERE ComponentId = @ComponentId";
        var row = await conn.QueryFirstOrDefaultAsync<ComponentStateRow>(
            new CommandDefinition(sql, new { ComponentId = componentId }, cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> componentIds, CancellationToken ct = default)
    {
        var ids = componentIds.ToList();
        if (ids.Count == 0) return [];

        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM ComponentStates WHERE ComponentId IN @Ids";
        var rows = await conn.QueryAsync<ComponentStateRow>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM ComponentStates";
        var rows = await conn.QueryAsync<ComponentStateRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO ComponentStates
                (ComponentId, Status, LastHeartbeatAt, LastStatusChangedAt, SubIndicatorsJson)
            VALUES
                (@ComponentId, @Status, @LastHeartbeatAt, @LastStatusChangedAt, @SubIndicatorsJson)
            ON CONFLICT(ComponentId) DO UPDATE SET
                Status              = excluded.Status,
                LastHeartbeatAt     = excluded.LastHeartbeatAt,
                LastStatusChangedAt = excluded.LastStatusChangedAt,
                SubIndicatorsJson   = excluded.SubIndicatorsJson;
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            state.ComponentId,
            Status              = state.Status.ToString(),
            LastHeartbeatAt     = state.LastHeartbeatAt?.ToString("O"),
            LastStatusChangedAt = state.LastStatusChangedAt.ToString("O"),
            SubIndicatorsJson   = SerializeSubIndicators(state.SubIndicators)
        }, cancellationToken: ct));
    }

    private static string? SerializeSubIndicators(IReadOnlyList<SubIndicator> indicators)
    {
        if (indicators.Count == 0) return null;
        var dtos = indicators.Select(s => new SubIndicatorDto(
            s.Name,
            s.Status.ToString(),
            s.Metric is null ? null : new MetricDto(s.Metric.Label, s.Metric.Value)));
        return JsonSerializer.Serialize(dtos);
    }

    private static ComponentState MapToDomain(ComponentStateRow row)
    {
        var state = new ComponentState(row.ComponentId);
        state.UpdateStatus(
            Enum.Parse<ComponentStatus>(row.Status),
            DateTimeOffset.Parse(row.LastStatusChangedAt));

        if (row.LastHeartbeatAt is not null)
            state.RecordHeartbeat(DateTimeOffset.Parse(row.LastHeartbeatAt));

        if (row.SubIndicatorsJson is not null)
        {
            var dtos = JsonSerializer.Deserialize<SubIndicatorDto[]>(row.SubIndicatorsJson) ?? [];
            var indicators = dtos.Select(d =>
            {
                MetricValue? metric = d.Metric is null
                    ? null
                    : new MetricValue(d.Metric.Label, d.Metric.Value);
                return new SubIndicator(d.Name, Enum.Parse<SubIndicatorStatus>(d.Status), metric);
            });
            state.SetSubIndicators(indicators);
        }

        return state;
    }

    private sealed record ComponentStateRow(
        string  ComponentId,
        string  Status,
        string? LastHeartbeatAt,
        string  LastStatusChangedAt,
        string? SubIndicatorsJson);

    private sealed record SubIndicatorDto(string Name, string Status, MetricDto? Metric);
    private sealed record MetricDto(string Label, decimal Value);
}
