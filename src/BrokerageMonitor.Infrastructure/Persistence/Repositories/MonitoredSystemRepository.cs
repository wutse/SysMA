using System.Text.Json;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class MonitoredSystemRepository : IMonitoredSystemRepository
{
    private readonly IDbConnectionFactory _factory;

    public MonitoredSystemRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM MonitoredSystems WHERE SystemId = @SystemId";
        var row = await conn.QueryFirstOrDefaultAsync<MonitoredSystemRow>(
            new CommandDefinition(sql, new { SystemId = systemId }, cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM MonitoredSystems WHERE IsActive = 1";
        var rows = await conn.QueryAsync<MonitoredSystemRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO MonitoredSystems
                (SystemId, Name, MarketStart, MarketEnd, AlertRecipients,
                 IsMaintenanceActive, MaintenanceOperator, IsActive, CreatedAt, UpdatedAt)
            VALUES
                (@SystemId, @Name, @MarketStart, @MarketEnd, @AlertRecipients,
                 @IsMaintenanceActive, @MaintenanceOperator, @IsActive, @Now, @Now)
            ON CONFLICT(SystemId) DO UPDATE SET
                Name                = excluded.Name,
                MarketStart         = excluded.MarketStart,
                MarketEnd           = excluded.MarketEnd,
                AlertRecipients     = excluded.AlertRecipients,
                IsMaintenanceActive = excluded.IsMaintenanceActive,
                MaintenanceOperator = excluded.MaintenanceOperator,
                IsActive            = excluded.IsActive,
                UpdatedAt           = excluded.UpdatedAt;
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            system.SystemId,
            system.Name,
            MarketStart = system.MarketSession.StartTime.ToString("HH:mm"),
            MarketEnd = system.MarketSession.EndTime.ToString("HH:mm"),
            AlertRecipients = JsonSerializer.Serialize(system.AlertRecipients.Select(e => e.Value)),
            IsMaintenanceActive = system.IsMaintenanceActive ? 1 : 0,
            system.MaintenanceOperator,
            IsActive = system.IsActive ? 1 : 0,
            Now = DateTimeOffset.UtcNow.ToString("O")
        }, cancellationToken: ct));
    }

    private static MonitoredSystem MapToDomain(MonitoredSystemRow row)
    {
        var marketSession = new MarketSessionWindow(
            TimeOnly.Parse(row.MarketStart),
            TimeOnly.Parse(row.MarketEnd));

        var system = new MonitoredSystem(
            row.SystemId, row.Name, marketSession,
            null, row.IsActive == 1);

        var recipients = JsonSerializer.Deserialize<string[]>(row.AlertRecipients)?
            .Select(e => new EmailAddress(e)) ?? [];
        system.SetAlertRecipients(recipients);

        if (row.IsMaintenanceActive == 1 && !string.IsNullOrWhiteSpace(row.MaintenanceOperator))
            system.ActivateMaintenance(row.MaintenanceOperator);

        return system;
    }

    private sealed record MonitoredSystemRow(
        string SystemId,
        string Name,
        string MarketStart,
        string MarketEnd,
        string AlertRecipients,
        long IsMaintenanceActive,
        string? MaintenanceOperator,
        long IsActive,
        string CreatedAt,
        string UpdatedAt);
}
