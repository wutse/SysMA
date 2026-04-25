using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ReadModels;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Persistence;
using BrokerageMonitor.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;

namespace BrokerageMonitor.Infrastructure.Tests.Persistence;

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Shared in-memory SQLite connection factory for integration tests.
/// Uses a named shared-memory database so that each call to <see cref="CreateConnection"/>
/// returns an independent, disposable connection while the anchor connection keeps the
/// in-memory database alive for the lifetime of the factory.
/// </summary>
internal sealed class TestInMemoryConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly SqliteConnection _anchor;   // keeps the named in-memory DB alive

    public TestInMemoryConnectionFactory()
    {
        _anchor = new SqliteConnection(BuildConnectionString());
        _anchor.Open();
    }

    public IDbConnection CreateConnection()
    {
        var conn = new SqliteConnection(BuildConnectionString());
        conn.Open();
        return conn;
    }

    private string BuildConnectionString() =>
        $"Data Source={_dbName};Mode=Memory;Cache=Shared";

    public void Dispose() => _anchor.Dispose();
}

// ---------------------------------------------------------------------------
// MonitoredSystemRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class MonitoredSystemRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IMonitoredSystemRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new MonitoredSystemRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task UpsertAsync_NewSystem_CanBeRetrievedById()
    {
        // Arrange
        var system = CreateSystem("SYS-01");

        // Act
        await _repo.UpsertAsync(system);
        var retrieved = await _repo.GetByIdAsync("SYS-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("SYS-01", retrieved.SystemId);
        Assert.AreEqual("Test System", retrieved.Name);
    }

    [TestMethod]
    public async Task UpsertAsync_ExistingSystem_UpdatesFields()
    {
        // Arrange
        var system = CreateSystem("SYS-01");
        await _repo.UpsertAsync(system);
        system.Rename("Renamed System");

        // Act
        await _repo.UpsertAsync(system);
        var retrieved = await _repo.GetByIdAsync("SYS-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Renamed System", retrieved.Name);
    }

    [TestMethod]
    public async Task GetAllActiveAsync_ReturnsOnlyActiveSystems()
    {
        // Arrange
        var active = CreateSystem("SYS-ACTIVE");
        var inactive = CreateSystem("SYS-INACTIVE");
        inactive.Deactivate();
        await _repo.UpsertAsync(active);
        await _repo.UpsertAsync(inactive);

        // Act
        var result = await _repo.GetAllActiveAsync();

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("SYS-ACTIVE", result[0].SystemId);
    }

    [TestMethod]
    public async Task SetMaintenanceModeAsync_ToTrue_UpdatesDatabase()
    {
        // Arrange
        var system = CreateSystem("SYS-01");
        await _repo.UpsertAsync(system);

        // Act
        await _repo.SetMaintenanceModeAsync("SYS-01", true);
        var retrieved = await _repo.GetByIdAsync("SYS-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.IsTrue(retrieved.IsMaintenanceActive);
    }

    [TestMethod]
    public async Task UpsertAsync_WithAlertRecipients_PreservesEmails()
    {
        // Arrange
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        var system = new MonitoredSystem("SYS-01", "Test", session,
            [new EmailAddress("a@b.com"), new EmailAddress("c@d.com")]);
        await _repo.UpsertAsync(system);

        // Act
        var retrieved = await _repo.GetByIdAsync("SYS-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.HasCount(2, retrieved.AlertRecipients);
        Assert.AreEqual("a@b.com", retrieved.AlertRecipients[0].Value);
    }

    private static MonitoredSystem CreateSystem(string id)
    {
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        return new MonitoredSystem(id, "Test System", session);
    }
}

// ---------------------------------------------------------------------------
// MonitoredComponentRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class MonitoredComponentRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IMonitoredSystemRepository _systemRepo = default!;
    private IMonitoredComponentRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _systemRepo = new MonitoredSystemRepository(_factory);
        _repo = new MonitoredComponentRepository(_factory);

        // Seed a parent system for FK constraints
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        await _systemRepo.UpsertAsync(new MonitoredSystem("SYS-01", "System", session));
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task UpsertAsync_ServiceComponent_CanBeRetrievedById()
    {
        // Arrange
        var component = new MonitoredComponent(
            "COMP-01", "SYS-01", "Service A", ComponentType.Service, "topic.a", 30);

        // Act
        await _repo.UpsertAsync(component);
        var retrieved = await _repo.GetByIdAsync("COMP-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("COMP-01", retrieved.ComponentId);
        Assert.AreEqual(ComponentType.Service, retrieved.ComponentType);
    }

    [TestMethod]
    public async Task UpsertAsync_ScheduledJobWithCron_PreservesCronExpression()
    {
        // Arrange
        var component = new MonitoredComponent(
            "JOB-01", "SYS-01", "Nightly Job", ComponentType.ScheduledJob, "topic.job", 60,
            cronExpression: "0 2 * * *");

        // Act
        await _repo.UpsertAsync(component);
        var retrieved = await _repo.GetByIdAsync("JOB-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("0 2 * * *", retrieved.CronExpression);
    }

    [TestMethod]
    public async Task UpsertAsync_WithMailParsingRule_RoundTripsKeywords()
    {
        // Arrange
        var rule = new MailParsingRule("sender@co.com", "REPORT", ["SUCCESS"], ["FAIL"]);
        var component = new MonitoredComponent(
            "COMP-MAIL", "SYS-01", "Mail Comp", ComponentType.Service, "topic.mail", 30,
            mailParsingRule: rule);

        // Act
        await _repo.UpsertAsync(component);
        var retrieved = await _repo.GetByIdAsync("COMP-MAIL");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.IsNotNull(retrieved.MailParsingRule);
        Assert.AreEqual("sender@co.com", retrieved.MailParsingRule.FromPattern);
        CollectionAssert.AreEqual(new[] { "SUCCESS" }, retrieved.MailParsingRule.SuccessKeywords.ToArray());
        CollectionAssert.AreEqual(new[] { "FAIL" }, retrieved.MailParsingRule.FailureKeywords.ToArray());
    }

    [TestMethod]
    public async Task GetBySystemIdAsync_ReturnsOnlyComponentsForSystem()
    {
        // Arrange
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        await _systemRepo.UpsertAsync(new MonitoredSystem("SYS-02", "Other", session));

        await _repo.UpsertAsync(new MonitoredComponent("COMP-A", "SYS-01", "A", ComponentType.Service, "ta", 30));
        await _repo.UpsertAsync(new MonitoredComponent("COMP-B", "SYS-02", "B", ComponentType.Service, "tb", 30));

        // Act
        var result = await _repo.GetBySystemIdAsync("SYS-01");

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("COMP-A", result[0].ComponentId);
    }
}

// ---------------------------------------------------------------------------
// ComponentStateRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class ComponentStateRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IComponentStateRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new ComponentStateRepository(_factory);

        // Seed FK dependencies
        var sysFactory = new MonitoredSystemRepository(_factory);
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        await sysFactory.UpsertAsync(new MonitoredSystem("SYS-01", "Sys", session));
        var compRepo = new MonitoredComponentRepository(_factory);
        await compRepo.UpsertAsync(new MonitoredComponent("COMP-01", "SYS-01", "C", ComponentType.Service, "t", 30));
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task UpsertAsync_NewState_CanBeRetrieved()
    {
        // Arrange
        var state = new ComponentState("COMP-01", ComponentStatus.Running);

        // Act
        await _repo.UpsertAsync(state);
        var retrieved = await _repo.GetByComponentIdAsync("COMP-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(ComponentStatus.Running, retrieved.Status);
    }

    [TestMethod]
    public async Task UpsertAsync_UpdatesExistingState()
    {
        // Arrange
        var state = new ComponentState("COMP-01", ComponentStatus.Running);
        await _repo.UpsertAsync(state);
        state.UpdateStatus(ComponentStatus.Error, DateTimeOffset.UtcNow);

        // Act
        await _repo.UpsertAsync(state);
        var retrieved = await _repo.GetByComponentIdAsync("COMP-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(ComponentStatus.Error, retrieved.Status);
    }

    [TestMethod]
    public async Task UpsertAsync_WithSubIndicators_RoundTripsJson()
    {
        // Arrange
        var state = new ComponentState("COMP-01");
        state.SetSubIndicators([new SubIndicator("CPU", SubIndicatorStatus.Normal, new MetricValue("usage", 12.5m))]);
        await _repo.UpsertAsync(state);

        // Act
        var retrieved = await _repo.GetByComponentIdAsync("COMP-01");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.HasCount(1, retrieved.SubIndicators);
        Assert.AreEqual("CPU", retrieved.SubIndicators[0].Name);
        Assert.AreEqual(12.5m, retrieved.SubIndicators[0].Metric!.Value);
    }

    [TestMethod]
    public async Task GetByComponentIdsAsync_BatchFetch_ReturnsMatchingStates()
    {
        // Arrange - need extra component
        var compRepo = new MonitoredComponentRepository(_factory);
        await compRepo.UpsertAsync(new MonitoredComponent("COMP-02", "SYS-01", "D", ComponentType.Service, "t2", 30));

        await _repo.UpsertAsync(new ComponentState("COMP-01", ComponentStatus.Running));
        await _repo.UpsertAsync(new ComponentState("COMP-02", ComponentStatus.Error));

        // Act
        var result = await _repo.GetByComponentIdsAsync(["COMP-01", "COMP-02"]);

        // Assert
        Assert.HasCount(2, result);
    }
}

// ---------------------------------------------------------------------------
// AlertRecordRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AlertRecordRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IAlertRecordRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new AlertRecordRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task AddAsync_Alert_CanBeRetrievedAsUnacknowledged()
    {
        // Arrange
        var alert = new AlertRecord(Guid.NewGuid(), "SYS-01", "COMP-01", ComponentStatus.Lost, DateTimeOffset.UtcNow);

        // Act
        await _repo.AddAsync(alert);
        var result = await _repo.GetUnacknowledgedAsync();

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual(alert.AlertId, result[0].AlertId);
    }

    [TestMethod]
    public async Task AcknowledgeBySystemAsync_ClearsGlobalFlag()
    {
        // Arrange
        await _repo.AddAsync(new AlertRecord(Guid.NewGuid(), "SYS-01", "COMP-01", ComponentStatus.Lost, DateTimeOffset.UtcNow));

        // Act
        await _repo.AcknowledgeBySystemAsync("SYS-01", "operator");
        var unacked = await _repo.GetUnacknowledgedAsync();
        var hasFlag = await _repo.HasUnacknowledgedAlertAsync("SYS-01");

        // Assert
        Assert.IsEmpty(unacked);
        Assert.IsFalse(hasFlag);
    }

    [TestMethod]
    public async Task GetHistoryAsync_WithTimeRange_FiltersCorrectly()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        await _repo.AddAsync(new AlertRecord(Guid.NewGuid(), "SYS-01", "COMP-01", ComponentStatus.Error,
            now.AddHours(-2)));
        await _repo.AddAsync(new AlertRecord(Guid.NewGuid(), "SYS-01", "COMP-01", ComponentStatus.Error,
            now.AddDays(-2)));

        // Act
        var result = await _repo.GetHistoryAsync("SYS-01", now.AddHours(-3), now);

        // Assert
        Assert.HasCount(1, result);
    }
}

// ---------------------------------------------------------------------------
// DailyExecutionRepository integration tests (BI-012, BI-013)
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DailyExecutionRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IDailyExecutionRepository _repo = default!;
    private static readonly Guid DefinitionId = Guid.NewGuid();

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new DailyExecutionRepository(_factory);

        // Seed FK dependencies
        var sysRepo = new MonitoredSystemRepository(_factory);
        var session = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0));
        await sysRepo.UpsertAsync(new MonitoredSystem("SYS-01", "Sys", session));

        var defRepo = new HealthMonitorDefinitionRepository(_factory);
        var compRepo = new MonitoredComponentRepository(_factory);
        await compRepo.UpsertAsync(new MonitoredComponent("COMP-01", "SYS-01", "Service A",
            ComponentType.Service, "tcp://localhost:5555", 60));
        var def = new HealthMonitorDefinition(
            DefinitionId, "SYS-01", "EOD Check",
            new TimeOnly(18, 0),
            new HealthRuleSchedule(ScheduleType.Daily),
            [new WatchedComponent("COMP-01", ComponentType.Service)]);
        await defRepo.UpsertAsync(def);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task AddAsync_Execution_CanBeRetrievedByDefinitionAndDate()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var execution = new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date);

        // Act
        await _repo.AddAsync(execution);
        var retrieved = await _repo.GetByDefinitionAndDateAsync(DefinitionId, date);

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(DailyExecutionStatus.InProgress, retrieved.Status);
    }

    [TestMethod]
    public async Task AddAsync_DuplicateDefinitionDate_ThrowsSqliteException()
    {
        // Arrange — BI-012: unique per definition per day
        var date = DateOnly.FromDateTime(DateTime.Today);
        await _repo.AddAsync(new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date));

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await _repo.AddAsync(new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date)));
    }

    [TestMethod]
    public async Task UpdateStatusAsync_ToSuccess_TransitionsFromInProgress()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var exec = new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date);
        await _repo.AddAsync(exec);

        // Act
        await _repo.UpdateStatusAsync(exec.ExecutionId, DailyExecutionStatus.Success,
            DateTimeOffset.UtcNow, null, null);
        var retrieved = await _repo.GetByDefinitionAndDateAsync(DefinitionId, date);

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(DailyExecutionStatus.Success, retrieved.Status);
    }

    [TestMethod]
    public async Task UpdateStatusAsync_OnAlreadyTerminal_DoesNotOverwrite()
    {
        // Arrange — BI-013: terminal state cannot be overwritten
        var date = DateOnly.FromDateTime(DateTime.Today);
        var exec = new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date);
        await _repo.AddAsync(exec);
        await _repo.UpdateStatusAsync(exec.ExecutionId, DailyExecutionStatus.Success,
            DateTimeOffset.UtcNow, null, null);

        // Act — attempt to overwrite with Failed
        await _repo.UpdateStatusAsync(exec.ExecutionId, DailyExecutionStatus.Failed,
            DateTimeOffset.UtcNow, null, null);
        var retrieved = await _repo.GetByDefinitionAndDateAsync(DefinitionId, date);

        // Assert — still Success
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(DailyExecutionStatus.Success, retrieved.Status);
    }

    [TestMethod]
    public async Task AddCompletedComponentAsync_AddsComponentIdAtomically()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var exec = new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date);
        await _repo.AddAsync(exec);

        // Act
        await _repo.AddCompletedComponentAsync(exec.ExecutionId, "COMP-01");
        await _repo.AddCompletedComponentAsync(exec.ExecutionId, "COMP-01"); // idempotent
        var retrieved = await _repo.GetByDefinitionAndDateAsync(DefinitionId, date);

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.HasCount(1, retrieved.CompletedComponents);
        Assert.AreEqual("COMP-01", retrieved.CompletedComponents[0]);
    }

    [TestMethod]
    public async Task AddCompletedComponentAsync_OnTerminalExecution_DoesNotUpdate()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.Today);
        var exec = new DailyExecution(Guid.NewGuid(), DefinitionId, "SYS-01", date);
        await _repo.AddAsync(exec);
        await _repo.UpdateStatusAsync(exec.ExecutionId, DailyExecutionStatus.Success,
            DateTimeOffset.UtcNow, null, null);

        // Act
        await _repo.AddCompletedComponentAsync(exec.ExecutionId, "COMP-01");
        var retrieved = await _repo.GetByDefinitionAndDateAsync(DefinitionId, date);

        // Assert — completed list empty because execution was terminal
        Assert.IsNotNull(retrieved);
        Assert.IsEmpty(retrieved.CompletedComponents);
    }

    [TestMethod]
    public async Task UpdateStatusAsync_NonTerminalStatus_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _repo.UpdateStatusAsync(Guid.NewGuid(), DailyExecutionStatus.InProgress,
                DateTimeOffset.UtcNow, null, null));
    }
}

// ---------------------------------------------------------------------------
// ExecutionHistoryRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class ExecutionHistoryRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IExecutionHistoryRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new ExecutionHistoryRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task AddAsync_Entry_CanBeQueriedByTimeRange()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var entry = new ExecutionHistoryEntry(
            Guid.NewGuid(), "SYS-01", "COMP-01", "Service A",
            ComponentType.Service, ComponentStatus.Completed,
            now.AddMinutes(-5), now, null);
        await _repo.AddAsync(entry);

        // Act
        var result = await _repo.QueryAsync("SYS-01", now.AddHours(-1), now.AddMinutes(1));

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("Service A", result[0].ComponentName);
    }

    [TestMethod]
    public async Task DeleteOlderThanAsync_RemovesOldEntries()
    {
        // Arrange
        var old = DateTimeOffset.UtcNow.AddDays(-31);
        await _repo.AddAsync(new ExecutionHistoryEntry(
            Guid.NewGuid(), "SYS-01", "COMP-01", "Old Service",
            ComponentType.Service, ComponentStatus.Completed, old, old.AddMinutes(1), null));

        // Act
        await _repo.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30));
        var result = await _repo.QueryAsync(null, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        // Assert
        Assert.IsEmpty(result);
    }
}

// ---------------------------------------------------------------------------
// AuditLogRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AuditLogRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private IAuditLogRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new AuditLogRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task AddAsync_Entry_CanBeQueried()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var entry = new AuditLogEntry(
            Guid.NewGuid(), "SYS-01", "COMP-01",
            "StateOverride", "alice", "maintenance check", now);
        await _repo.AddAsync(entry);

        // Act
        var result = await _repo.QueryAsync("SYS-01", now.AddSeconds(-1), now.AddSeconds(1));

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("alice", result[0].OperatorName);
    }

    [TestMethod]
    public async Task DeleteOlderThanAsync_RemovesStaleEntries()
    {
        // Arrange
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await _repo.AddAsync(new AuditLogEntry(
            Guid.NewGuid(), "SYS-01", null, "Login", "bob", null, cutoff.AddDays(-1)));

        // Act
        await _repo.DeleteOlderThanAsync(cutoff);
        var result = await _repo.QueryAsync(null, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        // Assert
        Assert.IsEmpty(result);
    }
}

// ---------------------------------------------------------------------------
// NotificationInboxRepository integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class NotificationInboxRepositoryTests
{
    private TestInMemoryConnectionFactory _factory = default!;
    private INotificationInboxRepository _repo = default!;

    [TestInitialize]
    public async Task InitAsync()
    {
        _factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitialiseAsync();
        _repo = new NotificationInboxRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task AddAsync_Item_AppearsInGetAll()
    {
        // Arrange
        var item = new NotificationInboxItem(
            Guid.NewGuid(), Guid.NewGuid(), null,
            "EOD Report", "All jobs completed.",
            NotificationType.HealthSuccess, DateTimeOffset.UtcNow);
        await _repo.AddAsync(item);

        // Act
        var all = await _repo.GetAllAsync();

        // Assert
        Assert.HasCount(1, all);
        Assert.AreEqual("EOD Report", all[0].Title);
        Assert.IsFalse(all[0].IsRead);
    }

    [TestMethod]
    public async Task MarkAsReadAsync_SetsIsReadToTrue()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        await _repo.AddAsync(new NotificationInboxItem(
            itemId, Guid.NewGuid(), null,
            "Title", "Body", NotificationType.HealthFailure, DateTimeOffset.UtcNow));

        // Act
        await _repo.MarkAsReadAsync(itemId);
        var all = await _repo.GetAllAsync();

        // Assert
        Assert.IsTrue(all[0].IsRead);
    }

    [TestMethod]
    public async Task DeleteOlderThanAsync_RemovesOldItems()
    {
        // Arrange
        var old = DateTimeOffset.UtcNow.AddDays(-31);
        await _repo.AddAsync(new NotificationInboxItem(
            Guid.NewGuid(), Guid.NewGuid(), null,
            "Old", "Old item", NotificationType.HealthExempted, old));

        // Act
        await _repo.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-30));
        var all = await _repo.GetAllAsync();

        // Assert
        Assert.IsEmpty(all);
    }
}

// ---------------------------------------------------------------------------
// DatabaseInitializer integration tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DatabaseInitializerIntegrationTests
{
    [TestMethod]
    public async Task InitialiseAsync_CreatesAllExpectedTables()
    {
        // Arrange
        using var factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

        // Act
        await initializer.InitialiseAsync();

        // Assert — verify all 11 tables exist
        using var conn = factory.CreateConnection();
        const string sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));

        var expected = new[]
        {
            "AlertRecords", "AuditLogs", "ComponentStates", "DailyExecutions",
            "ExecutionHistory", "HealthDefinitionComponents", "HealthMonitorDefinitions",
            "MonitoredComponents", "MonitoredSystems", "NotificationInbox"
        };

        foreach (var table in expected)
            Assert.Contains(table, tables, $"Table '{table}' was not created.");
    }

    [TestMethod]
    public async Task InitialiseAsync_IsIdempotent_SecondCallDoesNotThrow()
    {
        // Arrange
        using var factory = new TestInMemoryConnectionFactory();
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

        // Act & Assert — no exception
        await initializer.InitialiseAsync();
        await initializer.InitialiseAsync();
    }
}
