using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

// ---------------------------------------------------------------------------
// Hand-rolled test doubles
// ---------------------------------------------------------------------------

internal sealed class FakeComponentRepository : IMonitoredComponentRepository
{
    private readonly Dictionary<string, MonitoredComponent> _store = new();

    public void Add(MonitoredComponent c) => _store[c.ComponentId] = c;

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(_store.Values.Where(c => c.SystemId == systemId).ToList());

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(_store.Values.Where(c => c.IsActive).ToList());

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
    {
        _store[component.ComponentId] = component;
        return Task.CompletedTask;
    }
}

internal sealed class FakeStateRepository : IComponentStateRepository
{
    private readonly Dictionary<string, ComponentState> _store = new();

    public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(IEnumerable<string> componentIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>(
            componentIds.Select(id => _store.GetValueOrDefault(id)).OfType<ComponentState>().ToList());

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([.. _store.Values]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        _store[state.ComponentId] = state;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTimerRegistry : IHeartbeatTimerRegistry
{
    public List<string> RegisteredComponents { get; } = [];
    public List<string> ResetComponents { get; } = [];
    public List<string> UnregisteredComponents { get; } = [];

    public void RegisterComponent(string componentId, string systemId, ComponentType componentType, int timeoutSeconds)
        => RegisteredComponents.Add(componentId);

    public void ResetTimer(string componentId) => ResetComponents.Add(componentId);
    public void UnregisterComponent(string componentId) => UnregisteredComponents.Add(componentId);
}

internal sealed class FakeEventDispatcher : IDomainEventDispatcher
{
    public List<IDomainEvent> Dispatched { get; } = [];

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        Dispatched.Add(@event);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class HeartbeatProcessorTests
{
    private static MonitoredComponent BuildServiceComponent(
        string componentId = "COMP-01",
        string systemId = "SYS-01",
        bool isActive = true) =>
        new(componentId, systemId, "Test Service",
            ComponentType.Service, "topic.test", 30,
            isActive: isActive);

    private static MonitoredComponent BuildScheduledJobComponent(
        string componentId = "JOB-01",
        string systemId = "SYS-01") =>
        new(componentId, systemId, "Test Job",
            ComponentType.ScheduledJob, "topic.job", 60,
            cronExpression: "0 0 * * *");

    private static HeartbeatMessage BuildMessage(
        string componentId = "COMP-01",
        string systemId = "SYS-01",
        string status = "Normal",
        IReadOnlyList<SubIndicatorPayload>? subIndicators = null) =>
        new("Heartbeat", systemId, componentId,
            DateTimeOffset.UtcNow, status, null, subIndicators);

    private (HeartbeatProcessor sut,
             FakeComponentRepository repo,
             FakeStateRepository stateRepo,
             ComponentStateCache cache,
             FakeTimerRegistry timers,
             FakeEventDispatcher events) CreateSut()
    {
        var repo = new FakeComponentRepository();
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var timers = new FakeTimerRegistry();
        var events = new FakeEventDispatcher();
        var sut = new HeartbeatProcessor(repo, stateRepo, cache, timers, events,
            NullLogger<HeartbeatProcessor>.Instance);
        return (sut, repo, stateRepo, cache, timers, events);
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessAsync_NewComponent_TransitionsFromUnknownToNormal()
    {
        // Arrange
        var (sut, repo, _, cache, _, events) = CreateSut();
        repo.Add(BuildServiceComponent());

        // Act
        await sut.ProcessAsync(BuildMessage(status: "Normal"));

        // Assert — state updated
        var state = cache.GetState("COMP-01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Normal, state.Status);

        // Assert — ComponentStatusChanged raised
        var changed = events.Dispatched.OfType<ComponentStatusChanged>().Single();
        Assert.AreEqual(ComponentStatus.Unknown, changed.PreviousStatus);
        Assert.AreEqual(ComponentStatus.Normal, changed.NewStatus);
    }

    [TestMethod]
    public async Task ProcessAsync_StatusUnchanged_DoesNotRaiseComponentStatusChanged()
    {
        // Arrange
        var (sut, repo, _, cache, _, events) = CreateSut();
        repo.Add(BuildServiceComponent());
        cache.SetState(new ComponentState("COMP-01", ComponentStatus.Normal));

        // Act — send same status
        await sut.ProcessAsync(BuildMessage(status: "Normal"));

        // Assert — no ComponentStatusChanged
        Assert.IsFalse(events.Dispatched.OfType<ComponentStatusChanged>().Any());
    }

    [TestMethod]
    public async Task ProcessAsync_AnyHeartbeat_AlwaysRaisesComponentHeartbeatReceived()
    {
        // Arrange
        var (sut, repo, _, _, _, events) = CreateSut();
        repo.Add(BuildServiceComponent());

        // Act
        await sut.ProcessAsync(BuildMessage(status: "Normal"));

        // Assert
        Assert.IsTrue(events.Dispatched.OfType<ComponentHeartbeatReceived>().Any());
    }

    [TestMethod]
    public async Task ProcessAsync_StoppedStatus_DoesNotResetTimer()
    {
        // Arrange
        var (sut, repo, _, _, timers, _) = CreateSut();
        repo.Add(BuildServiceComponent());

        // Act
        await sut.ProcessAsync(BuildMessage(status: "Stopped"));

        // Assert — registered but NOT reset (Stopped exemption, BI-011)
        Assert.Contains("COMP-01", timers.RegisteredComponents);
        Assert.DoesNotContain("COMP-01", timers.ResetComponents);
    }

    [TestMethod]
    public async Task ProcessAsync_NonStoppedStatus_ResetsTimer()
    {
        // Arrange
        var (sut, repo, _, _, timers, _) = CreateSut();
        repo.Add(BuildServiceComponent());

        // Act
        await sut.ProcessAsync(BuildMessage(status: "Warning"));

        // Assert
        Assert.Contains("COMP-01", timers.ResetComponents);
    }

    [TestMethod]
    public async Task ProcessAsync_SubIndicatorError_ElevatesStatusToError()
    {
        // Arrange
        var (sut, repo, _, cache, _, events) = CreateSut();
        repo.Add(BuildServiceComponent());

        var subIndicators = new List<SubIndicatorPayload>
        {
            new("CPU", "Error", null),
            new("Memory", "Normal", null)
        }.AsReadOnly();

        // Act — reported Normal but sub-indicator says Error
        await sut.ProcessAsync(BuildMessage(status: "Normal", subIndicators: subIndicators));

        // Assert — status rolled up to Error
        var state = cache.GetState("COMP-01");
        Assert.AreEqual(ComponentStatus.Error, state!.Status);

        var changed = events.Dispatched.OfType<ComponentStatusChanged>().Single();
        Assert.AreEqual(ComponentStatus.Error, changed.NewStatus);
    }

    [TestMethod]
    public async Task ProcessAsync_AllSubIndicatorsNormal_UsesReportedStatus()
    {
        // Arrange
        var (sut, repo, _, cache, _, _) = CreateSut();
        repo.Add(BuildServiceComponent());

        var subIndicators = new List<SubIndicatorPayload>
        {
            new("CPU", "Normal", null),
            new("Memory", "Normal", null)
        }.AsReadOnly();

        // Act
        await sut.ProcessAsync(BuildMessage(status: "Warning", subIndicators: subIndicators));

        // Assert
        Assert.AreEqual(ComponentStatus.Warning, cache.GetState("COMP-01")!.Status);
    }

    [TestMethod]
    public async Task ProcessAsync_UnknownComponent_SkipsProcessing()
    {
        // Arrange
        var (sut, _, _, cache, timers, events) = CreateSut();
        // No component registered

        // Act
        await sut.ProcessAsync(BuildMessage());

        // Assert — nothing persisted, no timers, no events
        Assert.IsNull(cache.GetState("COMP-01"));
        Assert.IsEmpty(timers.RegisteredComponents);
        Assert.IsEmpty(events.Dispatched);
    }

    [TestMethod]
    public async Task ProcessAsync_InactiveComponent_SkipsProcessing()
    {
        // Arrange
        var (sut, repo, _, cache, _, events) = CreateSut();
        repo.Add(BuildServiceComponent(isActive: false));

        // Act
        await sut.ProcessAsync(BuildMessage());

        // Assert
        Assert.IsNull(cache.GetState("COMP-01"));
        Assert.IsEmpty(events.Dispatched);
    }

    [TestMethod]
    public async Task ProcessAsync_InvalidStatusString_SkipsProcessing()
    {
        // Arrange
        var (sut, repo, _, cache, _, events) = CreateSut();
        repo.Add(BuildServiceComponent());

        // Act
        await sut.ProcessAsync(BuildMessage(status: "INVALID_XYZ"));

        // Assert
        Assert.IsNull(cache.GetState("COMP-01"));
        Assert.IsEmpty(events.Dispatched);
    }

    [TestMethod]
    public async Task ProcessAsync_ScheduledJob_RegistersComponent()
    {
        // Arrange
        var (sut, repo, _, _, timers, _) = CreateSut();
        repo.Add(BuildScheduledJobComponent());

        // Act
        await sut.ProcessAsync(BuildMessage("JOB-01", status: "Running"));

        // Assert
        Assert.Contains("JOB-01", timers.RegisteredComponents);
    }

    // ── ComputeRolledUpStatus (internal static, tested directly) ────────────

    [TestMethod]
    public void ComputeRolledUpStatus_NoSubIndicators_ReturnsReported()
    {
        var result = HeartbeatProcessor.ComputeRolledUpStatus(ComponentStatus.Normal, []);
        Assert.AreEqual(ComponentStatus.Normal, result);
    }

    [TestMethod]
    public void ComputeRolledUpStatus_AllNormal_ReturnsReported()
    {
        var subs = new List<SubIndicator>
        {
            new("A", SubIndicatorStatus.Normal),
            new("B", SubIndicatorStatus.Normal)
        };
        var result = HeartbeatProcessor.ComputeRolledUpStatus(ComponentStatus.Warning, subs);
        Assert.AreEqual(ComponentStatus.Warning, result);
    }

    [TestMethod]
    public void ComputeRolledUpStatus_ErrorSubOnNormalReport_ReturnsError()
    {
        var subs = new List<SubIndicator> { new("A", SubIndicatorStatus.Error) };
        var result = HeartbeatProcessor.ComputeRolledUpStatus(ComponentStatus.Normal, subs);
        Assert.AreEqual(ComponentStatus.Error, result);
    }

    [TestMethod]
    public void ComputeRolledUpStatus_ErrorSubOnLostReport_ReturnsLost()
    {
        // Lost is higher severity than Error — should stay Lost
        var subs = new List<SubIndicator> { new("A", SubIndicatorStatus.Error) };
        var result = HeartbeatProcessor.ComputeRolledUpStatus(ComponentStatus.Lost, subs);
        Assert.AreEqual(ComponentStatus.Lost, result);
    }

    // ── GetSeverity ordering ─────────────────────────────────────────────────

    [TestMethod]
    public void GetSeverity_Lost_IsHighestNonMaintenance()
    {
        Assert.IsGreaterThan(
            HeartbeatProcessor.GetSeverity(ComponentStatus.Error),
            HeartbeatProcessor.GetSeverity(ComponentStatus.Lost));
    }

    [TestMethod]
    public void GetSeverity_Normal_IsLowestSeverity()
    {
        foreach (var status in new[]
        {
            ComponentStatus.Lost, ComponentStatus.Error, ComponentStatus.Warning,
            ComponentStatus.Unknown, ComponentStatus.Stopped, ComponentStatus.Idle,
            ComponentStatus.Running
        })
        {
            Assert.IsGreaterThan(
                HeartbeatProcessor.GetSeverity(ComponentStatus.Normal),
                HeartbeatProcessor.GetSeverity(status),
                $"{status} should have higher severity than Normal");
        }
    }

    /// <summary>
    /// N2: When no cached state exists for a component, the new ComponentState's
    /// LastStatusChangedAt must be seeded from message.Timestamp — not from
    /// the static DateTimeOffset.UtcNow domain fallback.
    /// </summary>
    [TestMethod]
    public async Task ProcessAsync_NewComponentWithNoCachedState_InitialTimestampFromMessage()
    {
        // Arrange
        var (sut, repo, _, cache, _, _) = CreateSut();
        repo.Add(BuildServiceComponent());

        var historicalTimestamp = new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var message = new HeartbeatMessage(
            "Heartbeat", "SYS-01", "COMP-01",
            historicalTimestamp, "Normal", null, null);

        // Act
        await sut.ProcessAsync(message);

        // Assert — the state was created using message.Timestamp, not DateTime.UtcNow
        var state = cache.GetState("COMP-01");
        Assert.IsNotNull(state);
        // After ProcessAsync the status changes Unknown → Normal, which calls UpdateStatus
        // with message.Timestamp — so LastStatusChangedAt must match.
        Assert.AreEqual(historicalTimestamp, state!.LastStatusChangedAt,
            "LastStatusChangedAt must be sourced from message.Timestamp (N2 fix)");
    }
}
