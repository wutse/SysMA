using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
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
             FakeAuditLogger audit) CreateSut()
    {
        var alert = new FakeAlertEvaluationService();
        var health = new FakeHealthEvaluationService();
        var realtime = new FakeRealtimeNotificationService();
        var audit = new FakeAuditLogger();
        var sut = new DomainEventDispatcher(alert, health, realtime, audit,
            NullLogger<DomainEventDispatcher>.Instance);
        return (sut, alert, health, realtime, audit);
    }

    // ── ComponentStatusChanged dispatch ──────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_ComponentStatusChanged_CallsAllFourHandlers()
    {
        var (sut, alert, health, realtime, audit) = CreateSut();
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
        var (sut, alert, health, realtime, audit) = CreateSut();
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
        var (sut, alert, health, realtime, audit) = CreateSut();
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
        var (sut, alert, _, _, _) = CreateSut();
        alert.ThrowOn = new InvalidOperationException("Fatal");

        // Must not throw
        await sut.DispatchAsync(BuildStatusChanged());
    }

    // ── ComponentLost dispatch ────────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_CallsAlertAndRealtimeAndAudit()
    {
        var (sut, alert, _, realtime, audit) = CreateSut();
        var evt = new ComponentLost("COMP-01", "SYS-01", DateTimeOffset.UtcNow);

        await sut.DispatchAsync(evt);

        Assert.ContainsSingle(alert.Received);
        Assert.ContainsSingle(realtime.StatusChanges);
        Assert.ContainsSingle(audit.Logs);
    }

    [TestMethod]
    public async Task DispatchAsync_ComponentLost_SyntheticEventHasLostStatus()
    {
        var (sut, alert, _, _, audit) = CreateSut();
        await sut.DispatchAsync(new ComponentLost("COMP-01", "SYS-01", DateTimeOffset.UtcNow));

        Assert.AreEqual(ComponentStatus.Lost, alert.Received[0].NewStatus);
        Assert.AreEqual(ComponentStatus.Lost, audit.Logs[0].Curr);
    }

    // ── Unknown event passthrough ────────────────────────────────────────────

    [TestMethod]
    public async Task DispatchAsync_UnhandledEventType_NoHandlersCalled()
    {
        var (sut, alert, health, realtime, audit) = CreateSut();

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
        var (sut, _, _, _, _) = CreateSut();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => sut.DispatchAsync<ComponentStatusChanged>(null!));
    }
}
