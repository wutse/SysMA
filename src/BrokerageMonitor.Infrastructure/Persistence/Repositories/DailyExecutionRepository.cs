using System.Reflection;
using System.Text.Json;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Dapper;

namespace BrokerageMonitor.Infrastructure.Persistence.Repositories;

public sealed class DailyExecutionRepository : IDailyExecutionRepository
{
    private static readonly IReadOnlySet<string> TerminalStatuses = new HashSet<string>
    {
        nameof(DailyExecutionStatus.Success),
        nameof(DailyExecutionStatus.Failed),
        nameof(DailyExecutionStatus.Missed),
        nameof(DailyExecutionStatus.Exempted)
    };

    private readonly IDbConnectionFactory _factory;

    public DailyExecutionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<DailyExecution?> GetByDefinitionAndDateAsync(
        Guid definitionId, DateOnly date, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT * FROM DailyExecutions
            WHERE DefinitionId = @DefinitionId AND ExecutionDate = @Date
            """;
        var row = await conn.QueryFirstOrDefaultAsync<DailyExecutionRow>(new CommandDefinition(sql, new
        {
            DefinitionId = definitionId.ToString(),
            Date         = date.ToString("yyyy-MM-dd")
        }, cancellationToken: ct));
        return row is null ? null : MapToDomain(row);
    }

    public async Task<IReadOnlyList<DailyExecution>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "SELECT * FROM DailyExecutions WHERE ExecutionDate = @Date";
        var rows = await conn.QueryAsync<DailyExecutionRow>(new CommandDefinition(
            sql, new { Date = date.ToString("yyyy-MM-dd") }, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<DailyExecution>> QueryHistoryAsync(
        Guid? definitionId, string? systemId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            SELECT * FROM DailyExecutions
            WHERE (@DefinitionId IS NULL OR DefinitionId = @DefinitionId)
              AND (@SystemId     IS NULL OR SystemId     = @SystemId)
              AND ExecutionDate >= @From
              AND ExecutionDate <= @To
            ORDER BY ExecutionDate DESC;
            """;
        var rows = await conn.QueryAsync<DailyExecutionRow>(new CommandDefinition(sql, new
        {
            DefinitionId = definitionId?.ToString(),
            SystemId     = systemId,
            From         = from.ToString("yyyy-MM-dd"),
            To           = to.ToString("yyyy-MM-dd")
        }, cancellationToken: ct));
        return rows.Select(MapToDomain).ToList();
    }

    public async Task AddAsync(DailyExecution execution, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = """
            INSERT INTO DailyExecutions
                (ExecutionId, DefinitionId, SystemId, ExecutionDate, Status,
                 CreatedAt, EvaluatedAt, FailedComponentsJson, CompletedComponentsJson,
                 MissedReason, NotificationSentAt)
            VALUES
                (@ExecutionId, @DefinitionId, @SystemId, @ExecutionDate, @Status,
                 @CreatedAt, @EvaluatedAt, @FailedComponentsJson, @CompletedComponentsJson,
                 @MissedReason, @NotificationSentAt);
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, ToParameters(execution), cancellationToken: ct));
    }

    public async Task UpdateStatusAsync(
        Guid executionId,
        DailyExecutionStatus status,
        DateTimeOffset evaluatedAt,
        IReadOnlyList<string>? failedComponents,
        DateTimeOffset? notificationSentAt,
        CancellationToken ct = default)
    {
        if (!TerminalStatuses.Contains(status.ToString()))
            throw new ArgumentException($"Status '{status}' is not a terminal status.", nameof(status));

        using var conn = _factory.CreateConnection();
        // BI-013: only update if not already in a terminal state
        const string sql = """
            UPDATE DailyExecutions
            SET Status               = @Status,
                EvaluatedAt          = @EvaluatedAt,
                FailedComponentsJson = @FailedComponentsJson,
                NotificationSentAt   = @NotificationSentAt
            WHERE ExecutionId = @ExecutionId
              AND Status NOT IN ('Success','Failed','Missed','Exempted');
            """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            ExecutionId          = executionId.ToString(),
            Status               = status.ToString(),
            EvaluatedAt          = evaluatedAt.ToString("O"),
            FailedComponentsJson = failedComponents is null or { Count: 0 }
                ? null
                : JsonSerializer.Serialize(failedComponents),
            NotificationSentAt   = notificationSentAt?.ToString("O")
        }, cancellationToken: ct));
    }

    public async Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        // Atomic upsert using SQLite JSON functions; only if still InProgress and not already listed
        const string sql = """
            UPDATE DailyExecutions
            SET CompletedComponentsJson = json_insert(
                    COALESCE(CompletedComponentsJson, '[]'),
                    '$[#]',
                    @ComponentId
                )
            WHERE ExecutionId = @ExecutionId
              AND Status = 'InProgress'
              AND NOT EXISTS (
                    SELECT 1 FROM json_each(COALESCE(CompletedComponentsJson, '[]'))
                    WHERE value = @ComponentId
              );
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            ExecutionId = executionId.ToString(),
            ComponentId = componentId
        }, cancellationToken: ct));
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        const string sql = "DELETE FROM DailyExecutions WHERE CreatedAt < @Cutoff";
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { Cutoff = cutoff.ToString("O") }, cancellationToken: ct));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static object ToParameters(DailyExecution e) => new
    {
        ExecutionId             = e.ExecutionId.ToString(),
        DefinitionId            = e.DefinitionId.ToString(),
        e.SystemId,
        ExecutionDate           = e.ExecutionDate.ToString("yyyy-MM-dd"),
        Status                  = e.Status.ToString(),
        CreatedAt               = e.CreatedAt.ToString("O"),
        EvaluatedAt             = e.EvaluatedAt?.ToString("O"),
        FailedComponentsJson    = e.FailedComponents.Count == 0
            ? null : JsonSerializer.Serialize(e.FailedComponents),
        CompletedComponentsJson = e.CompletedComponents.Count == 0
            ? null : JsonSerializer.Serialize(e.CompletedComponents),
        e.MissedReason,
        NotificationSentAt      = e.NotificationSentAt?.ToString("O")
    };

    private static DailyExecution MapToDomain(DailyExecutionRow row)
    {
        var status = Enum.Parse<DailyExecutionStatus>(row.Status);

        // Use InProgress initially so the public constructor doesn't reject the state;
        // then fix all private fields via reflection for accurate DB-to-domain mapping.
        var execution = new DailyExecution(
            Guid.Parse(row.ExecutionId),
            Guid.Parse(row.DefinitionId),
            row.SystemId,
            DateOnly.Parse(row.ExecutionDate),
            DailyExecutionStatus.InProgress,
            row.MissedReason);

        // Restore timestamp fields that the constructor set to UtcNow
        SetPrivateProperty(execution, "CreatedAt", DateTimeOffset.Parse(row.CreatedAt));

        if (status != DailyExecutionStatus.InProgress)
            SetPrivateProperty(execution, "Status", status);

        if (row.EvaluatedAt is not null)
            SetPrivateProperty(execution, "EvaluatedAt", (DateTimeOffset?)DateTimeOffset.Parse(row.EvaluatedAt));

        if (row.NotificationSentAt is not null)
            SetPrivateProperty(execution, "NotificationSentAt", (DateTimeOffset?)DateTimeOffset.Parse(row.NotificationSentAt));

        if (row.CompletedComponentsJson is not null)
        {
            var items = JsonSerializer.Deserialize<string[]>(row.CompletedComponentsJson) ?? [];
            AppendToPrivateList<string>(execution, "_completedComponents", items);
        }

        if (row.FailedComponentsJson is not null)
        {
            var items = JsonSerializer.Deserialize<string[]>(row.FailedComponentsJson) ?? [];
            AppendToPrivateList<string>(execution, "_failedComponents", items);
        }

        return execution;
    }

    private static void SetPrivateProperty(object target, string propertyName, object? value)
    {
        var prop = target.GetType().GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        prop.SetValue(target, value);
    }

    private static void AppendToPrivateList<T>(object target, string fieldName, IEnumerable<T> items)
    {
        var field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found.");
        var list = (List<T>)field.GetValue(target)!;
        list.AddRange(items);
    }

    private sealed record DailyExecutionRow(
        string  ExecutionId,
        string  DefinitionId,
        string  SystemId,
        string  ExecutionDate,
        string  Status,
        string  CreatedAt,
        string? EvaluatedAt,
        string? FailedComponentsJson,
        string? CompletedComponentsJson,
        string? MissedReason,
        string? NotificationSentAt);
}
