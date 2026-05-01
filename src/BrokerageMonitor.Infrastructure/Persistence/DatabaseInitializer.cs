using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.Persistence;

/// <summary>
/// Runs one-time database initialisation on startup:
/// enables SQLite WAL journal mode and creates all required tables
/// (via CREATE TABLE IF NOT EXISTS).
/// Follows SRP: sole responsibility is schema bootstrapping.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbConnectionFactory connectionFactory,
        ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Executes WAL pragma and creates all schema tables.
    /// Safe to call on every startup; all DDL uses IF NOT EXISTS.
    /// </summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DatabaseInitializer: beginning schema initialisation.");

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

        foreach (var ddl in SchemaDdl)
            await ExecuteAsync(connection, ddl, cancellationToken);

        ApplyMigrations(connection, cancellationToken);

        _logger.LogInformation("DatabaseInitializer: schema initialisation completed successfully.");
    }

    private static readonly string[] SchemaDdl =
    [
        """
        CREATE TABLE IF NOT EXISTS MonitoredSystems (
            SystemId            TEXT PRIMARY KEY,
            Name                TEXT NOT NULL,
            MarketStart         TEXT NOT NULL,
            MarketEnd           TEXT NOT NULL,
            AlertRecipients     TEXT NOT NULL DEFAULT '[]',
            IsMaintenanceActive INTEGER NOT NULL DEFAULT 0,
            MaintenanceOperator TEXT,
            IsActive            INTEGER NOT NULL DEFAULT 1,
            CreatedAt           TEXT NOT NULL,
            UpdatedAt           TEXT NOT NULL
        );
        """,

        """
        CREATE TABLE IF NOT EXISTS MonitoredComponents (
            ComponentId             TEXT PRIMARY KEY,
            SystemId                TEXT NOT NULL REFERENCES MonitoredSystems(SystemId),
            Name                    TEXT NOT NULL,
            ComponentType           TEXT NOT NULL,
            ZeroMQTopic             TEXT NOT NULL,
            HeartbeatTimeoutSeconds INTEGER NOT NULL,
            CronExpression          TEXT,
            IsActive                INTEGER NOT NULL DEFAULT 1,
            CreatedAt               TEXT NOT NULL,
            UpdatedAt               TEXT NOT NULL,
            MailFromPattern         TEXT,
            MailSubjectPattern      TEXT,
            MailSuccessKeywords     TEXT,
            MailFailureKeywords     TEXT
        );
        """,

        """
        CREATE TABLE IF NOT EXISTS ComponentStates (
            ComponentId         TEXT PRIMARY KEY REFERENCES MonitoredComponents(ComponentId),
            Status              TEXT NOT NULL,
            LastHeartbeatAt     TEXT,
            LastStatusChangedAt TEXT NOT NULL,
            SubIndicatorsJson   TEXT
        );
        """,

        """
        CREATE TABLE IF NOT EXISTS AlertRecords (
            AlertId            TEXT PRIMARY KEY,
            SystemId           TEXT NOT NULL,
            ComponentId        TEXT NOT NULL,
            AlertStatus        TEXT NOT NULL,
            OccurredAt         TEXT NOT NULL,
            IsGlobalFlagActive INTEGER NOT NULL DEFAULT 1,
            AcknowledgedBy     TEXT,
            AcknowledgedAt     TEXT
        );
        """,

        "CREATE INDEX IF NOT EXISTS idx_alert_system ON AlertRecords(SystemId, IsGlobalFlagActive);",

        """
        CREATE TABLE IF NOT EXISTS HealthMonitorDefinitions (
            DefinitionId    TEXT PRIMARY KEY,
            SystemId        TEXT NOT NULL REFERENCES MonitoredSystems(SystemId),
            Name            TEXT NOT NULL,
            DeadlineTime    TEXT NOT NULL,
            ScheduleType    TEXT NOT NULL,
            CronExpression  TEXT,
            DayOfWeek       INTEGER,
            EmailRecipients TEXT NOT NULL DEFAULT '[]',
            TeamsWebhookUrl TEXT,
            NotificationsEnabled INTEGER NOT NULL DEFAULT 0,
            IsActive        INTEGER NOT NULL DEFAULT 1
        );
        """,

        """
        CREATE TABLE IF NOT EXISTS HealthDefinitionComponents (
            DefinitionId  TEXT NOT NULL REFERENCES HealthMonitorDefinitions(DefinitionId),
            ComponentId   TEXT NOT NULL REFERENCES MonitoredComponents(ComponentId),
            ComponentType TEXT NOT NULL,
            PRIMARY KEY (DefinitionId, ComponentId)
        );
        """,

        """
        CREATE TABLE IF NOT EXISTS DailyExecutions (
            ExecutionId             TEXT PRIMARY KEY,
            DefinitionId            TEXT NOT NULL REFERENCES HealthMonitorDefinitions(DefinitionId),
            SystemId                TEXT NOT NULL,
            ExecutionDate           TEXT NOT NULL,
            Status                  TEXT NOT NULL,
            CreatedAt               TEXT NOT NULL,
            EvaluatedAt             TEXT,
            FailedComponentsJson    TEXT,
            CompletedComponentsJson TEXT,
            MissedReason            TEXT,
            NotificationSentAt      TEXT
        );
        """,

        "CREATE UNIQUE INDEX IF NOT EXISTS idx_dailyexec_def_date ON DailyExecutions(DefinitionId, ExecutionDate);",
        "CREATE INDEX IF NOT EXISTS idx_dailyexec_date ON DailyExecutions(ExecutionDate);",

        """
        CREATE TABLE IF NOT EXISTS ExecutionHistory (
            HistoryId     TEXT PRIMARY KEY,
            SystemId      TEXT NOT NULL,
            ComponentId   TEXT NOT NULL,
            ComponentName TEXT NOT NULL,
            ComponentType TEXT NOT NULL,
            ResultStatus  TEXT NOT NULL,
            StartedAt     TEXT NOT NULL,
            EndedAt       TEXT,
            Message       TEXT
        );
        """,

        "CREATE INDEX IF NOT EXISTS idx_history_time ON ExecutionHistory(SystemId, StartedAt);",

        """
        CREATE TABLE IF NOT EXISTS AuditLogs (
            LogId          TEXT PRIMARY KEY,
            SystemId       TEXT NOT NULL,
            ComponentId    TEXT,
            OperatorName   TEXT NOT NULL,
            ActionType     TEXT NOT NULL,
            PreviousStatus TEXT,
            NewStatus      TEXT,
            Reason         TEXT NOT NULL DEFAULT '',
            OccurredAt     TEXT NOT NULL
        );
        """,

        "CREATE INDEX IF NOT EXISTS idx_audit_time ON AuditLogs(SystemId, OccurredAt);",

        """
        CREATE TABLE IF NOT EXISTS NotificationInbox (
            InboxItemId      TEXT PRIMARY KEY,
            DefinitionId     TEXT NOT NULL,
            ExecutionId      TEXT,
            Title            TEXT NOT NULL,
            Body             TEXT NOT NULL,
            NotificationType TEXT NOT NULL,
            SentAt           TEXT NOT NULL,
            IsRead           INTEGER NOT NULL DEFAULT 0
        );
        """
    ];

    private static Task ExecuteAsync(
        System.Data.IDbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies idempotent column-rename migrations for existing databases.
    /// Safe to run on every startup.
    /// </summary>
    private static void ApplyMigrations(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Migration: rename SendOnFailure → NotificationsEnabled (2026-05-01)
        // Check if the old column still exists before renaming.
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(HealthMonitorDefinitions);";
        using var reader = pragma.ExecuteReader();
        bool hasSendOnFailure = false;
        while (reader.Read())
        {
            if (reader["name"] is string name &&
                string.Equals(name, "SendOnFailure", StringComparison.OrdinalIgnoreCase))
            {
                hasSendOnFailure = true;
                break;
            }
        }
        reader.Close();

        if (hasSendOnFailure)
        {
            using var renameCmd = connection.CreateCommand();
            renameCmd.CommandText =
                "ALTER TABLE HealthMonitorDefinitions RENAME COLUMN SendOnFailure TO NotificationsEnabled;";
            renameCmd.ExecuteNonQuery();
        }
    }
}
