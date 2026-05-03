using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.UseCases.Maintenance;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles (prefixed ToggleMaint_ to avoid cross-file conflicts)
// ---------------------------------------------------------------------------

internal sealed class ToggleMaint_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = new();

    public void Add(MonitoredSystem system) => _store[system.SystemId] = system;
    public MonitoredSystem? LastUpserted { get; private set; }

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        LastUpserted = system;
        _store[system.SystemId] = system;
        return Task.CompletedTask;
    }

}

internal sealed class ToggleMaint_ComponentRepo : IMonitoredComponentRepository
{
    private readonly List<MonitoredComponent> _components = new();
    public void Add(MonitoredComponent component) => _components.Add(component);

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_components.FirstOrDefault(c => c.ComponentId == componentId));

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
            _components.Where(c => c.SystemId == systemId).ToList());

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([.. _components]);

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class ToggleMaint_StateRepo : IComponentStateRepository
{
    private readonly Dictionary<string, ComponentState> _store = new();
    public List<ComponentState> UpsertedStates { get; } = new();
    public int BatchReadCallCount { get; private set; }
    public int SingleReadCallCount { get; private set; }

    public void Add(ComponentState state) => _store[state.ComponentId] = state;

    public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
    {
        SingleReadCallCount++;
        return Task.FromResult(_store.GetValueOrDefault(componentId));
    }

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
    {
        BatchReadCallCount++;
        return Task.FromResult<IReadOnlyList<ComponentState>>(
            ids.Select(id => _store.GetValueOrDefault(id)).Where(s => s != null).Select(s => s!).ToList());
    }

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([.. _store.Values]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        UpsertedStates.Add(state);
        _store[state.ComponentId] = state;
        return Task.CompletedTask;
    }
}

internal sealed class ToggleMaint_StateCache : IComponentStateCache
{
    private readonly Dictionary<string, ComponentState> _cache = new();
    public List<ComponentState> SetStateCalls { get; } = new();

    public ComponentState? GetState(string componentId) => _cache.GetValueOrDefault(componentId);

    public void SetState(ComponentState state)
    {
        SetStateCalls.Add(state);
        _cache[state.ComponentId] = state;
    }

    public IReadOnlyList<ComponentState> GetAllStates() => [.. _cache.Values];
    public void LoadAll(IEnumerable<ComponentState> states)
    {
        foreach (var s in states) _cache[s.ComponentId] = s;
    }
}

internal sealed class ToggleMaint_AuditLogger : IAuditLogger
{
    public List<(string SystemId, string ActionType, string OperatorName)> Logs { get; } = new();

    public Task LogStatusChangedAsync(
        string systemId, string componentId,
        ComponentStatus previous, ComponentStatus current,
        DateTimeOffset occurredAt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task LogOperatorActionAsync(
        string systemId, string? componentId, string actionType,
        string operatorName, string? reason, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        Logs.Add((systemId, actionType, operatorName));
        return Task.CompletedTask;
    }
}

internal sealed class ToggleMaint_RealtimeService : IRealtimeNotificationService
{
    public List<(string SystemId, bool IsActive)> MaintenanceChanges { get; } = new();

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
    {
        MaintenanceChanges.Add((systemId, isActive));
        return Task.CompletedTask;
    }

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Builder helpers
// ---------------------------------------------------------------------------

internal static class ToggleMaint_Builder
{
    private static readonly MarketSessionWindow Session =
        new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    public static MonitoredSystem System(string id = "SYS-1")
        => new(id, "Test System", Session, isActive: true);

    public static MonitoredComponent Component(string systemId = "SYS-1", string id = "COMP-1")
        => new(id, systemId, "TestService", ComponentType.Service, "zmq.test", 30);

    public static ComponentState State(string componentId = "COMP-1")
        => new(componentId);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class ToggleMaintenanceModeHandlerTests
{
    private readonly ToggleMaint_SystemRepo _systemRepo = new();
    private readonly ToggleMaint_ComponentRepo _componentRepo = new();
    private readonly ToggleMaint_StateRepo _stateRepo = new();
    private readonly ToggleMaint_StateCache _stateCache = new();
    private readonly ToggleMaint_AuditLogger _auditLogger = new();
    private readonly ToggleMaint_RealtimeService _realtimeService = new();
    private readonly ToggleMaintenanceModeHandler _sut;

    public ToggleMaintenanceModeHandlerTests()
    {
        _sut = new ToggleMaintenanceModeHandler(
            _systemRepo,
            _componentRepo,
            _stateRepo,
            _stateCache,
            _auditLogger,
            _realtimeService,
            TimeProvider.System,
            NullLogger<ToggleMaintenanceModeHandler>.Instance);

        _systemRepo.Add(ToggleMaint_Builder.System());
        _componentRepo.Add(ToggleMaint_Builder.Component());
        _stateRepo.Add(ToggleMaint_Builder.State());
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
        var command = new ToggleMaintenanceModeCommand("SYS-1", true, operatorName);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptySystemId_ThrowsArgumentException(string systemId)
    {
        var command = new ToggleMaintenanceModeCommand(systemId, true, "operator");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    // -------------------------------------------------------------------------
    // Unknown system
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_UnknownSystem_NoUpsertCalled()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-UNKNOWN", true, "operator");

        await _sut.HandleAsync(command);

        Assert.IsNull(_systemRepo.LastUpserted);
    }

    // -------------------------------------------------------------------------
    // Activate
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_Activate_SystemMaintenanceFlagSet()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.IsNotNull(_systemRepo.LastUpserted);
        Assert.IsTrue(_systemRepo.LastUpserted!.IsMaintenanceActive);
    }

    [TestMethod]
    public async Task HandleAsync_Activate_ComponentStatusSetToMaintenance()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _stateRepo.UpsertedStates);
        Assert.AreEqual(ComponentStatus.Maintenance, _stateRepo.UpsertedStates[0].Status);
    }

    [TestMethod]
    public async Task HandleAsync_Activate_AuditLogWrittenWithMaintenanceActivated()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _auditLogger.Logs);
        Assert.AreEqual("MaintenanceActivated", _auditLogger.Logs[0].ActionType);
        Assert.AreEqual("Alice", _auditLogger.Logs[0].OperatorName);
    }

    [TestMethod]
    public async Task HandleAsync_Activate_SignalRPushedWithActiveTrue()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _realtimeService.MaintenanceChanges);
        Assert.AreEqual("SYS-1", _realtimeService.MaintenanceChanges[0].SystemId);
        Assert.IsTrue(_realtimeService.MaintenanceChanges[0].IsActive);
    }

    // -------------------------------------------------------------------------
    // Deactivate
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_Deactivate_SystemMaintenanceFlagCleared()
    {
        // Pre-activate
        _systemRepo.LastUpserted?.ActivateMaintenance("setup");

        var command = new ToggleMaintenanceModeCommand("SYS-1", false, "Alice");

        await _sut.HandleAsync(command);

        Assert.IsNotNull(_systemRepo.LastUpserted);
        Assert.IsFalse(_systemRepo.LastUpserted!.IsMaintenanceActive);
    }

    [TestMethod]
    public async Task HandleAsync_Deactivate_ComponentStatusResetToUnknown()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", false, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _stateRepo.UpsertedStates);
        Assert.AreEqual(ComponentStatus.Unknown, _stateRepo.UpsertedStates[0].Status);
    }

    [TestMethod]
    public async Task HandleAsync_Deactivate_AuditLogWrittenWithMaintenanceDeactivated()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", false, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _auditLogger.Logs);
        Assert.AreEqual("MaintenanceDeactivated", _auditLogger.Logs[0].ActionType);
    }

    [TestMethod]
    public async Task HandleAsync_Deactivate_SignalRPushedWithActiveFalse()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", false, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _realtimeService.MaintenanceChanges);
        Assert.IsFalse(_realtimeService.MaintenanceChanges[0].IsActive);
    }

    // -------------------------------------------------------------------------
    // Multi-component
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_Activate_MultipleComponents_AllSetToMaintenance()
    {
        _componentRepo.Add(ToggleMaint_Builder.Component(id: "COMP-2"));
        _stateRepo.Add(ToggleMaint_Builder.State("COMP-2"));

        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(2, _stateRepo.UpsertedStates);
        Assert.IsTrue(_stateRepo.UpsertedStates.All(s => s.Status == ComponentStatus.Maintenance));
    }

    [TestMethod]
    public async Task HandleAsync_Activate_ComponentWithNoExistingState_CreatesNewState()
    {
        // COMP-2 has no pre-existing state record
        _componentRepo.Add(ToggleMaint_Builder.Component(id: "COMP-2"));

        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        var comp2State = _stateRepo.UpsertedStates.FirstOrDefault(s => s.ComponentId == "COMP-2");
        Assert.IsNotNull(comp2State);
        Assert.AreEqual(ComponentStatus.Maintenance, comp2State!.Status);
    }

    [TestMethod]
    public async Task HandleAsync_Activate_StateCacheUpdatedForAllComponents()
    {
        _componentRepo.Add(ToggleMaint_Builder.Component(id: "COMP-2"));
        _stateRepo.Add(ToggleMaint_Builder.State("COMP-2"));

        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(2, _stateCache.SetStateCalls);
        Assert.IsTrue(_stateCache.SetStateCalls.All(s => s.Status == ComponentStatus.Maintenance));
    }

    [TestMethod]
    public async Task HandleAsync_Deactivate_StateCacheUpdatedToUnknown()
    {
        var command = new ToggleMaintenanceModeCommand("SYS-1", false, "Alice");

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _stateCache.SetStateCalls);
        Assert.AreEqual(ComponentStatus.Unknown, _stateCache.SetStateCalls[0].Status);
    }

    /// <summary>
    /// N3: Component states must be loaded via a single GetByComponentIdsAsync batch call,
    /// not via individual GetByComponentIdAsync calls in a loop (N+1 anti-pattern).
    /// </summary>
    [TestMethod]
    public async Task HandleAsync_Activate_UsesGetByComponentIdsAsync_NoPlusOneReads()
    {
        // Arrange — add a second component to exercise multi-component path
        _componentRepo.Add(ToggleMaint_Builder.Component(id: "COMP-2"));
        _stateRepo.Add(ToggleMaint_Builder.State("COMP-2"));

        var command = new ToggleMaintenanceModeCommand("SYS-1", true, "Alice");

        // Act
        await _sut.HandleAsync(command);

        // Assert — exactly 1 batch read (N3 fix), zero individual reads
        Assert.AreEqual(1, _stateRepo.BatchReadCallCount,
            "States should be loaded via a single GetByComponentIdsAsync call");
        Assert.AreEqual(0, _stateRepo.SingleReadCallCount,
            "GetByComponentIdAsync must not be called in a loop (N+1 violation)");
    }
}
