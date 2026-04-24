using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.UseCases.StateOverride;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles (prefixed Override_ to avoid cross-file conflicts)
// ---------------------------------------------------------------------------

internal sealed class Override_ComponentRepo : IMonitoredComponentRepository
{
    private readonly Dictionary<string, MonitoredComponent> _store = new();
    public void Add(MonitoredComponent c) => _store[c.ComponentId] = c;

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
            _store.Values.Where(c => c.SystemId == systemId).ToList());

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class Override_StateRepo : IComponentStateRepository
{
    private readonly Dictionary<string, ComponentState> _store = new();
    public List<ComponentState> UpsertedStates { get; } = new();

    public void Add(ComponentState state) => _store[state.ComponentId] = state;

    public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>(
            ids.Select(id => _store.GetValueOrDefault(id)).Where(s => s != null).Select(s => s!).ToList());

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([.. _store.Values]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        UpsertedStates.Add(state);
        _store[state.ComponentId] = state;
        return Task.CompletedTask;
    }
}

internal sealed class Override_StateCache : IComponentStateCache
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

internal sealed class Override_EventDispatcher : IDomainEventDispatcher
{
    public List<IDomainEvent> DispatchedEvents { get; } = new();

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        DispatchedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

internal sealed class Override_AuditLogger : IAuditLogger
{
    public List<(string SystemId, string? ComponentId, string ActionType, string OperatorName, string? Reason)> Logs { get; } = new();

    public Task LogStatusChangedAsync(
        string systemId, string componentId,
        ComponentStatus previous, ComponentStatus current,
        DateTimeOffset occurredAt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task LogOperatorActionAsync(
        string systemId, string? componentId, string actionType,
        string operatorName, string? reason, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        Logs.Add((systemId, componentId, actionType, operatorName, reason));
        return Task.CompletedTask;
    }
}

internal sealed class Override_RealtimeService : IRealtimeNotificationService
{
    public List<ComponentStatusChanged> StatusChanges { get; } = new();

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
    {
        StatusChanges.Add(evt);
        return Task.CompletedTask;
    }

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class OverrideComponentStateHandlerTests
{
    private readonly Override_ComponentRepo _componentRepo = new();
    private readonly Override_StateRepo _stateRepo = new();
    private readonly Override_StateCache _stateCache = new();
    private readonly Override_EventDispatcher _dispatcher = new();
    private readonly Override_AuditLogger _auditLogger = new();
    private readonly Override_RealtimeService _realtimeService = new();
    private readonly OverrideComponentStateHandler _sut;

    public OverrideComponentStateHandlerTests()
    {
        _sut = new OverrideComponentStateHandler(
            _componentRepo,
            _stateRepo,
            _stateCache,
            _dispatcher,
            _auditLogger,
            _realtimeService,
            NullLogger<OverrideComponentStateHandler>.Instance);

        _componentRepo.Add(new MonitoredComponent("COMP-1", "SYS-1", "TestService", ComponentType.Service, "zmq.test", 30));
        _stateRepo.Add(new ComponentState("COMP-1"));
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
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, operatorName, "reason");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptyReason_ThrowsArgumentException(string reason)
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "operator", reason);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptyComponentId_ThrowsArgumentException(string componentId)
    {
        var command = new OverrideComponentStateCommand("SYS-1", componentId, ComponentStatus.Error, "operator", "reason");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HandleAsync_EmptySystemId_ThrowsArgumentException(string systemId)
    {
        var command = new OverrideComponentStateCommand(systemId, "COMP-1", ComponentStatus.Error, "operator", "reason");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(command));
    }

    // -------------------------------------------------------------------------
    // Unknown component
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_UnknownComponent_NoUpsertCalled()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-UNKNOWN", ComponentStatus.Error, "operator", "reason");

        await _sut.HandleAsync(command);

        Assert.AreEqual(0, _stateRepo.UpsertedStates.Count);
    }

    // -------------------------------------------------------------------------
    // Successful override
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task HandleAsync_ValidCommand_ComponentStateUpdated()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "Force error");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _stateRepo.UpsertedStates.Count);
        Assert.AreEqual(ComponentStatus.Error, _stateRepo.UpsertedStates[0].Status);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_ComponentStateOverriddenEventDispatched()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "Force error");

        await _sut.HandleAsync(command);

        var evt = _dispatcher.DispatchedEvents.OfType<ComponentStateOverridden>().SingleOrDefault();
        Assert.IsNotNull(evt);
        Assert.AreEqual("COMP-1", evt!.ComponentId);
        Assert.AreEqual("SYS-1", evt.SystemId);
        Assert.AreEqual(ComponentStatus.Unknown, evt.PreviousStatus);
        Assert.AreEqual(ComponentStatus.Error, evt.NewStatus);
        Assert.AreEqual("Alice", evt.OperatorName);
        Assert.AreEqual("Force error", evt.Reason);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_AuditLogWrittenWithReasonAndComponentId()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "Force error");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _auditLogger.Logs.Count);
        var log = _auditLogger.Logs[0];
        Assert.AreEqual("SYS-1", log.SystemId);
        Assert.AreEqual("COMP-1", log.ComponentId);
        Assert.AreEqual("ComponentStateOverridden", log.ActionType);
        Assert.AreEqual("Alice", log.OperatorName);
        Assert.AreEqual("Force error", log.Reason);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_SignalRPushedWithStatusChange()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "Force error");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _realtimeService.StatusChanges.Count);
        var evt = _realtimeService.StatusChanges[0];
        Assert.AreEqual("COMP-1", evt.ComponentId);
        Assert.AreEqual(ComponentStatus.Error, evt.NewStatus);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_SignalRIncludesPreviousStatus()
    {
        // Initial state is Unknown; override to Error
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "reason");

        await _sut.HandleAsync(command);

        Assert.AreEqual(ComponentStatus.Unknown, _realtimeService.StatusChanges[0].PreviousStatus);
    }

    [TestMethod]
    public async Task HandleAsync_ComponentWithNoExistingState_CreatesNewState()
    {
        // COMP-2 has no pre-existing state record
        _componentRepo.Add(new MonitoredComponent("COMP-2", "SYS-1", "Other", ComponentType.Service, "zmq.other", 30));

        var command = new OverrideComponentStateCommand("SYS-1", "COMP-2", ComponentStatus.Warning, "Alice", "reason");

        await _sut.HandleAsync(command);

        var upserted = _stateRepo.UpsertedStates.FirstOrDefault(s => s.ComponentId == "COMP-2");
        Assert.IsNotNull(upserted);
        Assert.AreEqual(ComponentStatus.Warning, upserted!.Status);
    }

    [TestMethod]
    public async Task HandleAsync_ValidCommand_StateCacheUpdatedWithNewStatus()
    {
        var command = new OverrideComponentStateCommand("SYS-1", "COMP-1", ComponentStatus.Error, "Alice", "reason");

        await _sut.HandleAsync(command);

        Assert.AreEqual(1, _stateCache.SetStateCalls.Count);
        Assert.AreEqual(ComponentStatus.Error, _stateCache.SetStateCalls[0].Status);
    }
}
