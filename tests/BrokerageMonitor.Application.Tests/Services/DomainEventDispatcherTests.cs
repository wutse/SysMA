using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

// ---------------------------------------------------------------------------
// Fake downstream handlers
// ---------------------------------------------------------------------------

internal sealed class FakeAlertEvaluationService : IAlertEvaluationService
{
    public List<ComponentStatusChanged> Received { get; } = [];
    public Exception? ThrowOn { get; set; }

    public Task EvaluateAsync(ComponentStatusChanged evt, CancellationToken ct = default)
    {
        if (ThrowOn is not null) throw ThrowOn;
        Received.Add(evt);
        return Task.CompletedTask;
    }
}

internal sealed class FakeHealthEvaluationService : IAggregateHealthEvaluationService
{
    public List<ComponentStatusChanged> Received { get; } = [];

    public Task UpdateComponentProgressAsync(ComponentStatusChanged evt, CancellationToken ct = default)
    {
        Received.Add(evt);
        return Task.CompletedTask;
    }

    public Task EvaluateDefinitionAsync(Guid definitionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakeRealtimeNotificationService : IRealtimeNotificationService
{
    public List<ComponentStatusChanged> StatusChanges { get; } = [];

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

internal sealed class FakeAuditLogger : IAuditLogger
{
    public List<(string SystemId, string ComponentId, ComponentStatus Prev, ComponentStatus Curr)> Logs { get; } = [];

    public Task LogStatusChangedAsync(
        string systemId, string componentId,
        ComponentStatus previous, ComponentStatus current,
        DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        Logs.Add((systemId, componentId, previous, current));
        return Task.CompletedTask;
    }

    public Task LogOperatorActionAsync(
        string systemId, string? componentId, string actionType,
        string operatorName, string? reason, DateTimeOffset occurredAt,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeDispatcherStateCache : IComponentStateCache
{
    private readonly Dictionary<string, ComponentState> _cache = new();

    public void Add(ComponentState state) => _cache[state.ComponentId] = state;

    public ComponentState? GetState(string componentId) => _cache.GetValueOrDefault(componentId);
    public void SetState(ComponentState state) => _cache[state.ComponentId] = state;
    public IReadOnlyList<ComponentState> GetAllStates() => [.. _cache.Values];
    public void LoadAll(IEnumerable<ComponentState> states)
    {
        foreach (var s in states) _cache[s.ComponentId] = s;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DomainEventDispatcherTests
{
    private static ComponentStatusChanged BuildStatusChanged(
        ComponentStatus previous = ComponentStatus.Unknown,
        ComponentStatus current = ComponentStatus.Error) =>
        new("COMP-01", "SYS-01", previous, current, DateTimeOffset.UtcNow);

    private (DomainEventDispatcher sut,
             FakeAlertEvaluationService alert,
             FakeHealthEvaluationService health,
             FakeRealtimeNotificationService realtime,
             FakeAuditLogger audit,
             FakeDispatcherStateCache cache) CreateSut()
    {
        var alert = new FakeAlertEvaluationService();
        var health = new FakeHealthEvaluationService();
        var realtime = new FakeRealtimeNotificationService();
        var audit = new FakeAuditLogger();
        var cache = new FakeDispatcherStateCache();
        var sut = new DomainEventDispatcher(alert, health, realtime, audit, cache,
            NullLogger<DomainEventDispatcher>.Instance);
        return (sut, alert, health, realtime, audit, cache);
    }

    // ── ComponentStatusChanged dispatch ──────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_ComponentStatusChanged_CallsAllFourHandlers()
    {
        var (sut, alert, health, realtime, audit, _) = CreateSut();
        var evt = BuildStatusChanged();

        await sut.DispatchAsync(evt);

        Assert.ContainsSingle(alert.Received);
        Assert.ContainsSingle(health.Received);
        Assert.ContainsSingle(realtime.StatusChanges);
        Assert.ContainsSingle(audit.Logs);
    }

    [TestMethod]
    public async Task DispatchAsync_ComponentStatusChanged_PassesCorrectEventToHandlers()
    {
        var (sut, alert, health, realtime, audit, _) = CreateSut();
        var evt = BuildStatusChanged(ComponentStatus.Normal, ComponentStatus.Lost);

        await sut.DispatchAsync(evt);

        Assert.AreEqual(evt, alert.Received[0]);
        Assert.AreEqual(evt, health.Received[0]);
        Assert.AreEqual(ComponentStatus.Lost, audit.Logs[0].Curr);
    }

    // ── Failure isolation ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_AlertHandlerThrows_OtherHandlersStillCalled()
    {
        var (sut, alert, health, realtime, audit, _) = CreateSut();
        alert.ThrowOn = new InvalidOperationException("Simulated failure");
        var evt = BuildStatusChanged();

        // Should NOT throw even though alert handler fails
        await sut.DispatchAsync(evt);

        Assert.ContainsSingle(health.Received);
        Assert.ContainsSingle(realtime.StatusChanges);
        Assert.ContainsSingle(audit.Logs);
    }

    [TestMethod]
    public async Task DispatchAsync_AlertHandlerThrows_DoesNotPropagateException()
    {
        var (sut, alert, _, _, _, _) = CreateSut();
        alert.ThrowOn = new InvalidOperationException("Fatal");

        // Must not throw
        await sut.DispatchAsync(BuildStatusChanged());
    }

    // ── ComponentLost dispatch ────────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_CallsAlertAndRealtimeAndAudit()
    {
        var (sut, alert, _, realtime, audit, _) = CreateSut();
        var evt = new ComponentLost("COMP-01", "SYS-01", DateTimeOffset.UtcNow);

        await sut.DispatchAsync(evt);

        Assert.ContainsSingle(alert.Received);
        Assert.ContainsSingle(realtime.StatusChanges);
        Assert.ContainsSingle(audit.Logs);
    }

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_SyntheticEventHasLostStatus()
    {
        var (sut, alert, _, _, audit, _) = CreateSut();
        await sut.DispatchAsync(new ComponentLost("COMP-01", "SYS-01", DateTimeOffset.UtcNow));

        Assert.AreEqual(ComponentStatus.Lost, alert.Received[0].NewStatus);
        Assert.AreEqual(ComponentStatus.Lost, audit.Logs[0].Curr);
    }

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_PreviousStatusReadFromCache()
    {
        var (sut, alert, _, _, audit, cache) = CreateSut();
        var cachedState = new ComponentState("COMP-01");
        cachedState.UpdateStatus(ComponentStatus.Normal, DateTimeOffset.UtcNow);
        cache.Add(cachedState);

        await sut.DispatchAsync(new ComponentLost("COMP-01", "SYS-01", DateTimeOffset.UtcNow));

        Assert.AreEqual(ComponentStatus.Normal, alert.Received[0].PreviousStatus);
        Assert.AreEqual(ComponentStatus.Normal, audit.Logs[0].Prev);
    }

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_PreviousStatusFallsBackToUnknownWhenNotCached()
    {
        var (sut, alert, _, _, audit, _) = CreateSut(); // cache empty
        await sut.DispatchAsync(new ComponentLost("COMP-99", "SYS-01", DateTimeOffset.UtcNow));

        Assert.AreEqual(ComponentStatus.Unknown, alert.Received[0].PreviousStatus);
        Assert.AreEqual(ComponentStatus.Unknown, audit.Logs[0].Prev);
    }

    // ── Unknown event passthrough ────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_UnhandledEventType_NoHandlersCalled()
    {
        var (sut, alert, health, realtime, audit, _) = CreateSut();

        // Pass an event type not handled by the dispatcher
        await sut.DispatchAsync(new AlertTriggered(Guid.NewGuid(), "SYS-01", "COMP-01",
            ComponentStatus.Error, DateTimeOffset.UtcNow));

        Assert.IsEmpty(alert.Received);
        Assert.IsEmpty(health.Received);
        Assert.IsEmpty(realtime.StatusChanges);
        Assert.IsEmpty(audit.Logs);
    }

    // ── Null guard ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_NullEvent_ThrowsArgumentNullException()
    {
        var (sut, _, _, _, _, _) = CreateSut();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => sut.DispatchAsync<ComponentStatusChanged>(null!));
    }
}
