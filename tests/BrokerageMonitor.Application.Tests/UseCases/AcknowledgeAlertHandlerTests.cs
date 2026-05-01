using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.UseCases.Alerts;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles (prefixed AckAlert_ to avoid cross-file conflicts)
// ---------------------------------------------------------------------------

internal sealed class AckAlert_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = new();

    public void Add(MonitoredSystem system) => _store[system.SystemId] = system;

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
        => Task.CompletedTask;

}

internal sealed class AckAlert_AlertRepo : IAlertRecordRepository
{
    public List<(string SystemId, string OperatorName)> AcknowledgeCalls { get; } = new();

    public Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>([]);

    public Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlySet<string>> GetSystemsWithUnacknowledgedAlertAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    public Task AddAsync(AlertRecord alert, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default)
    {
        AcknowledgeCalls.Add((systemId, operatorName));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>([]);
}

internal sealed class AckAlert_AuditLogger : IAuditLogger
{
    public List<(string SystemId, string? ComponentId, string ActionType, string OperatorName)> Logs { get; } = new();

    public Task LogStatusChangedAsync(
        string systemId, string componentId,
        ComponentStatus previous, ComponentStatus current,
        DateTimeOffset occurredAt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task LogOperatorActionAsync(
        string systemId, string? componentId, string actionType,
        string operatorName, string? reason, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        Logs.Add((systemId, componentId, actionType, operatorName));
        return Task.CompletedTask;
    }
}

internal sealed class AckAlert_RealtimeService : IRealtimeNotificationService
{
    public List<string> AcknowledgedSystemIds { get; } = new();

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
    {
        AcknowledgedSystemIds.Add(systemId);
        return Task.CompletedTask;
    }

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AcknowledgeAlertHandlerTests
{
    private static readonly MarketSessionWindow Session =
        new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    private readonly AckAlert_SystemRepo _systemRepo = new();
    private readonly AckAlert_AlertRepo _alertRepo = new();
    private readonly AckAlert_AuditLogger _auditLogger = new();
    private readonly AckAlert_RealtimeService _realtimeService = new();
    private readonly AcknowledgeAlertHandler _sut;

    public AcknowledgeAlertHandlerTests()
    {
        _sut = new AcknowledgeAlertHandler(
            _alertRepo,
            _systemRepo,
            _auditLogger,
            _realtimeService,
            NullLogger<AcknowledgeAlertHandler>.Instance);

        _systemRepo.Add(new MonitoredSystem("SYS-1", "Test System", Session, isActive: true));
    }

    // -------------------------------------------------------------------------
    // Validation guards
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.HandleAsync(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptyOperatorName_ThrowsArgumentException(string operatorName)
    {
        var command = new AcknowledgeAlertCommand("SYS-1", operatorName);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptySystemId_ThrowsArgumentException(string systemId)
    {
        var command = new AcknowledgeAlertCommand(systemId, "operator");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    // -------------------------------------------------------------------------
    // Unknown system
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_UnknownSystem_NoAcknowledgeCalled()
    {
        var command = new AcknowledgeAlertCommand("SYS-UNKNOWN", "operator");

        await _sut.HandleAsync(command);

        Assert.AreEqual(0, _alertRepo.AcknowledgeCalls.Count);
    }

    // -------------------------------------------------------------------------
    // Successful acknowledgement
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_ValidCommand_AcknowledgeBySystemCalled()
    {
        var command = new AcknowledgeAlertCommand("SYS-1", "Alice");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _alertRepo.AcknowledgeCalls.Count);
        Assert.AreEqual("SYS-1", _alertRepo.AcknowledgeCalls[0].SystemId);
        Assert.AreEqual("Alice", _alertRepo.AcknowledgeCalls[0].OperatorName);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_AuditLogWritten()
    {
        var command = new AcknowledgeAlertCommand("SYS-1", "Alice");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _auditLogger.Logs.Count);
        var log = _auditLogger.Logs[0];
        Assert.AreEqual("SYS-1", log.SystemId);
        Assert.AreEqual("AlertAcknowledged", log.ActionType);
        Assert.AreEqual("Alice", log.OperatorName);
        Assert.IsNull(log.ComponentId);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_SignalRPushCalled()
    {
        var command = new AcknowledgeAlertCommand("SYS-1", "Alice");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _realtimeService.AcknowledgedSystemIds.Count);
        Assert.AreEqual("SYS-1", _realtimeService.AcknowledgedSystemIds[0]);
    }
}
