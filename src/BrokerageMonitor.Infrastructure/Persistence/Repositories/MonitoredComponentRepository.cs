using System.Text.Json;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class MonitoredComponentRepository : IMonitoredComponentRepository
{
    private readonly IDbConnectionFactory _factory;

    public MonitoredComponentRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM MonitoredComponents WHERE ComponentId = @ComponentId";
        var row = await conn.QueryFirstOrDefaultAsync<MonitoredComponentRow>(
            new CommandDefinition(sql, new { ComponentId = componentId }, cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM MonitoredComponents WHERE SystemId = @SystemId";
        var rows = await conn.QueryAsync<MonitoredComponentRow>(
            new CommandDefinition(sql, new { SystemId = systemId }, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM MonitoredComponents WHERE IsActive = 1";
        var rows = await conn.QueryAsync<MonitoredComponentRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO MonitoredComponents
                (ComponentId, SystemId, Name, ComponentType, ZeroMQTopic,
                 HeartbeatTimeoutSeconds, CronExpression, IsActive, CreatedAt, UpdatedAt,
                 MailFromPattern, MailSubjectPattern, MailSuccessKeywords, MailFailureKeywords)
            VALUES
                (@ComponentId, @SystemId, @Name, @ComponentType, @ZeroMQTopic,
                 @HeartbeatTimeoutSeconds, @CronExpression, @IsActive, @Now, @Now,
                 @MailFromPattern, @MailSubjectPattern, @MailSuccessKeywords, @MailFailureKeywords)
            ON CONFLICT(ComponentId) DO UPDATE SET
                Name                    = excluded.Name,
                ComponentType           = excluded.ComponentType,
                ZeroMQTopic             = excluded.ZeroMQTopic,
                HeartbeatTimeoutSeconds = excluded.HeartbeatTimeoutSeconds,
                CronExpression          = excluded.CronExpression,
                IsActive                = excluded.IsActive,
                UpdatedAt               = excluded.UpdatedAt,
                MailFromPattern         = excluded.MailFromPattern,
                MailSubjectPattern      = excluded.MailSubjectPattern,
                MailSuccessKeywords     = excluded.MailSuccessKeywords,
                MailFailureKeywords     = excluded.MailFailureKeywords;
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            component.ComponentId,
            component.SystemId,
            component.Name,
            ComponentType           = component.ComponentType.ToString(),
            component.ZeroMQTopic,
            component.HeartbeatTimeoutSeconds,
            component.CronExpression,
            IsActive                = component.IsActive ? 1 : 0,
            Now                     = DateTimeOffset.UtcNow.ToString("O"),
            MailFromPattern         = component.MailParsingRule?.FromPattern,
            MailSubjectPattern      = component.MailParsingRule?.SubjectPattern,
            MailSuccessKeywords     = component.MailParsingRule is null
                ? null
                : JsonSerializer.Serialize(component.MailParsingRule.SuccessKeywords),
            MailFailureKeywords     = component.MailParsingRule is null
                ? null
                : JsonSerializer.Serialize(component.MailParsingRule.FailureKeywords)
        }, cancellationToken: ct));
    }

    private static MonitoredComponent MapToDomain(MonitoredComponentRow row)
    {
        MailParsingRule? rule = null;
        if (row.MailFromPattern is not null && row.MailSubjectPattern is not null)
        {
            var success = JsonSerializer.Deserialize<string[]>(row.MailSuccessKeywords ?? "[]") ?? [];
            var failure = JsonSerializer.Deserialize<string[]>(row.MailFailureKeywords ?? "[]") ?? [];
            rule = new MailParsingRule(row.MailFromPattern, row.MailSubjectPattern, success, failure);
        }

        return new MonitoredComponent(
            row.ComponentId,
            row.SystemId,
            row.Name,
            Enum.Parse<ComponentType>(row.ComponentType),
            row.ZeroMQTopic,
            (int)row.HeartbeatTimeoutSeconds,
            row.CronExpression,
            rule,
            row.IsActive == 1);
    }

    private sealed record MonitoredComponentRow(
        string  ComponentId,
        string  SystemId,
        string  Name,
        string  ComponentType,
        string  ZeroMQTopic,
        long    HeartbeatTimeoutSeconds,
        string? CronExpression,
        long    IsActive,
        string  CreatedAt,
        string  UpdatedAt,
        string? MailFromPattern,
        string? MailSubjectPattern,
        string? MailSuccessKeywords,
        string? MailFailureKeywords);
}
