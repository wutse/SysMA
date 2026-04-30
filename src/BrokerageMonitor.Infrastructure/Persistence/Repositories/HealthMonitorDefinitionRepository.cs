using System.Text.Json;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class HealthMonitorDefinitionRepository : IHealthMonitorDefinitionRepository
{
    private readonly IDbConnectionFactory _factory;

    public HealthMonitorDefinitionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<HealthMonitorDefinition?> GetByIdAsync(Guid definitionId, CancellationToken ct = default)
    {
        using var conn = (SqliteConnection)_factory.CreateConnection();
        conn.Open();
        var defId = definitionId.ToString();

        const string mainSql = "SELECT * FROM HealthMonitorDefinitions WHERE DefinitionId = @DefinitionId";
        var row = await conn.QueryFirstOrDefaultAsync<DefinitionRow>(
            new CommandDefinition(mainSql, new { DefinitionId = defId }, cancellationToken: ct));
        if (row is null) return null;

        var components = await LoadComponentsAsync(conn, defId, ct);
        return MapToDomain(row, components);
    }

    public async Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default)
    {
        using var conn = (SqliteConnection)_factory.CreateConnection();
        conn.Open();

        const string mainSql = "SELECT * FROM HealthMonitorDefinitions WHERE IsActive = 1";
        var rows = (await conn.QueryAsync<DefinitionRow>(
            new CommandDefinition(mainSql, cancellationToken: ct))).ToList();

        return await BuildDefinitionsAsync(conn, rows, ct);
    }

    public async Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
    {
        using var conn = (SqliteConnection)_factory.CreateConnection();
        conn.Open();

        const string mainSql = "SELECT * FROM HealthMonitorDefinitions WHERE SystemId = @SystemId";
        var rows = (await conn.QueryAsync<DefinitionRow>(
            new CommandDefinition(mainSql, new { SystemId = systemId }, cancellationToken: ct))).ToList();

        return await BuildDefinitionsAsync(conn, rows, ct);
    }

    public async Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default)
    {
        using var conn = (SqliteConnection)_factory.CreateConnection();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            const string mainSql = """
                INSERT INTO HealthMonitorDefinitions
                    (DefinitionId, SystemId, Name, DeadlineTime, ScheduleType,
                     CronExpression, DayOfWeek, EmailRecipients, TeamsWebhookUrl, SendOnFailure, IsActive)
                VALUES
                    (@DefinitionId, @SystemId, @Name, @DeadlineTime, @ScheduleType,
                     @CronExpression, @DayOfWeek, @EmailRecipients, @TeamsWebhookUrl, @SendOnFailure, @IsActive)
                ON CONFLICT(DefinitionId) DO UPDATE SET
                    Name            = excluded.Name,
                    DeadlineTime    = excluded.DeadlineTime,
                    ScheduleType    = excluded.ScheduleType,
                    CronExpression  = excluded.CronExpression,
                    DayOfWeek       = excluded.DayOfWeek,
                    EmailRecipients = excluded.EmailRecipients,
                    TeamsWebhookUrl = excluded.TeamsWebhookUrl,
                    SendOnFailure   = excluded.SendOnFailure,
                    IsActive        = excluded.IsActive;
                """;

            var defId = definition.DefinitionId.ToString();
            await conn.ExecuteAsync(new CommandDefinition(mainSql, new
            {
                DefinitionId    = defId,
                definition.SystemId,
                definition.Name,
                DeadlineTime    = definition.DeadlineTime.ToString("HH:mm"),
                ScheduleType    = definition.Schedule.ScheduleType.ToString(),
                definition.Schedule.CronExpression,
                DayOfWeek       = definition.Schedule.DayOfWeek.HasValue
                    ? (int?)definition.Schedule.DayOfWeek.Value
                    : null,
                EmailRecipients = JsonSerializer.Serialize(
                    definition.EmailRecipients.Select(e => e.Value)),
                definition.TeamsWebhookUrl,
                SendOnFailure   = definition.NotificationsEnabled ? 1 : 0,
                IsActive        = definition.IsActive ? 1 : 0
            }, transaction: tx, cancellationToken: ct));

            // Refresh junction table
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM HealthDefinitionComponents WHERE DefinitionId = @DefinitionId",
                new { DefinitionId = defId }, transaction: tx, cancellationToken: ct));

            const string junctionSql = """
                INSERT INTO HealthDefinitionComponents (DefinitionId, ComponentId, ComponentType)
                VALUES (@DefinitionId, @ComponentId, @ComponentType);
                """;
            foreach (var wc in definition.WatchedComponents)
            {
                await conn.ExecuteAsync(new CommandDefinition(junctionSql, new
                {
                    DefinitionId  = defId,
                    wc.ComponentId,
                    ComponentType = wc.ComponentType.ToString()
                }, transaction: tx, cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(Guid definitionId, CancellationToken ct = default)
    {
        using var conn = (SqliteConnection)_factory.CreateConnection();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            var defId = definitionId.ToString();
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM HealthDefinitionComponents WHERE DefinitionId = @DefinitionId",
                new { DefinitionId = defId }, transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM HealthMonitorDefinitions WHERE DefinitionId = @DefinitionId",
                new { DefinitionId = defId }, transaction: tx, cancellationToken: ct));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<HealthMonitorDefinition>> BuildDefinitionsAsync(
        SqliteConnection conn, List<DefinitionRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var defIds = rows.Select(r => r.DefinitionId).ToList();
        const string junctionSql = "SELECT * FROM HealthDefinitionComponents WHERE DefinitionId IN @Ids";
        var allComponents = (await conn.QueryAsync<JunctionRow>(
            new CommandDefinition(junctionSql, new { Ids = defIds }, cancellationToken: ct))).ToList();

        return rows.Select(row =>
        {
            var components = allComponents
                .Where(c => c.DefinitionId == row.DefinitionId)
                .ToList();
            return MapToDomain(row, components);
        }).ToList();
    }

    private static async Task<List<JunctionRow>> LoadComponentsAsync(
        SqliteConnection conn, string definitionId, CancellationToken ct)
    {
        const string sql = "SELECT * FROM HealthDefinitionComponents WHERE DefinitionId = @DefinitionId";
        var result = await conn.QueryAsync<JunctionRow>(
            new CommandDefinition(sql, new { DefinitionId = definitionId }, cancellationToken: ct));
        return result.ToList();
    }

    private static HealthMonitorDefinition MapToDomain(DefinitionRow row, List<JunctionRow> components)
    {
        var schedule = new HealthRuleSchedule(
            Enum.Parse<ScheduleType>(row.ScheduleType),
            row.CronExpression,
            row.DayOfWeek.HasValue ? (DayOfWeek)row.DayOfWeek.Value : null);

        var watchedComponents = components.Select(c =>
            new WatchedComponent(c.ComponentId, Enum.Parse<ComponentType>(c.ComponentType)));

        var recipients = JsonSerializer.Deserialize<string[]>(row.EmailRecipients)?
            .Select(e => new EmailAddress(e)) ?? [];

        return new HealthMonitorDefinition(
            Guid.Parse(row.DefinitionId),
            row.SystemId,
            row.Name,
            TimeOnly.Parse(row.DeadlineTime),
            schedule,
            watchedComponents,
            recipients,
            row.TeamsWebhookUrl,
            notificationsEnabled: row.SendOnFailure == 1,
            isActive: row.IsActive == 1);
    }

    private sealed record DefinitionRow(
        string  DefinitionId,
        string  SystemId,
        string  Name,
        string  DeadlineTime,
        string  ScheduleType,
        string? CronExpression,
        long?   DayOfWeek,
        string  EmailRecipients,
        string? TeamsWebhookUrl,
        long    SendOnFailure,
        long    IsActive);

    private sealed record JunctionRow(
        string DefinitionId,
        string ComponentId,
        string ComponentType);
}
