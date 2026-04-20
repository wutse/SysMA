using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.Startup;
using BrokerageMonitor.Application.Tests.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Startup;

// ---------------------------------------------------------------------------
// Fake repositories (minimal implementation for startup tests)
// ---------------------------------------------------------------------------

internal sealed class FakeStartupStateRepository : IComponentStateRepository
{
    private readonly Dictionary<string, ComponentState> _store = new();

    public void Seed(ComponentState state) => _store[state.ComponentId] = state;

    public List<ComponentState> Upserted { get; } = [];

    public Task<ComponentState?> GetByComponentIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>(
            ids.Select(id => _store.GetValueOrDefault(id)).OfType<ComponentState>().ToList());

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([.. _store.Values]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        _store[state.ComponentId] = state;
        Upserted.Add(state);
        return Task.CompletedTask;
    }
}

internal sealed class FakeStartupComponentRepository : IMonitoredComponentRepository
{
    private readonly List<MonitoredComponent> _components = [];

    public void Add(MonitoredComponent c) => _components.Add(c);

    public Task<MonitoredComponent?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_components.FirstOrDefault(c => c.ComponentId == id));

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
            _components.Where(c => c.SystemId == systemId).ToList());

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
            _components.Where(c => c.IsActive).ToList());

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
    {
        _components.RemoveAll(c => c.ComponentId == component.ComponentId);
        _components.Add(component);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class StationStartupRecoveryServiceTests
{
    private static MonitoredComponent BuildService(
        string id = "SVC-01", string systemId = "SYS-01") =>
        new(id, systemId, $"Service {id}", ComponentType.Service, "topic.svc", 30);

    private static MonitoredComponent BuildScheduledJob(
        string id = "JOB-01", string systemId = "SYS-01") =>
        new(id, systemId, $"Job {id}", ComponentType.ScheduledJob, "topic.job", 60,
            cronExpression: "0 8 * * *");

    private (StationStartupRecoveryService sut,
             FakeStartupStateRepository stateRepo,
             FakeStartupComponentRepository componentRepo,
             ComponentStateCache cache,
             FakeTimerRegistry timers,
             FakeEventDispatcher events) CreateSut()
    {
        var stateRepo = new FakeStartupStateRepository();
        var componentRepo = new FakeStartupComponentRepository();
        var cache = new ComponentStateCache();
        var timers = new FakeTimerRegistry();
        var events = new FakeEventDispatcher();
        var sut = new StationStartupRecoveryService(
            stateRepo, componentRepo, cache, timers, events,
            NullLogger<StationStartupRecoveryService>.Instance);
        return (sut, stateRepo, componentRepo, cache, timers, events);
    }

    // ── Cache hydration ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task RecoverAsync_LoadsPersistedStatesIntoCache()
    {
        var (sut, stateRepo, componentRepo, cache, _, _) = CreateSut();
        stateRepo.Seed(new ComponentState("SVC-01", ComponentStatus.Normal));
        stateRepo.Seed(new ComponentState("SVC-02", ComponentStatus.Warning));
        componentRepo.Add(BuildService("SVC-01"));
        componentRepo.Add(BuildService("SVC-02"));

        await sut.RecoverAsync();

        Assert.AreEqual(ComponentStatus.Normal, cache.GetState("SVC-01")!.Status);
        Assert.AreEqual(ComponentStatus.Warning, cache.GetState("SVC-02")!.Status);
    }

    // ── ScheduledJob Running → Warning transition ─────────────────────────────

    [TestMethod]
    public async Task RecoverAsync_ScheduledJobInRunning_TransitionsToWarning()
    {
        var (sut, stateRepo, componentRepo, cache, _, _) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Running));
        componentRepo.Add(BuildScheduledJob());

        await sut.RecoverAsync();

        Assert.AreEqual(ComponentStatus.Warning, cache.GetState("JOB-01")!.Status);
    }

    [TestMethod]
    public async Task RecoverAsync_ScheduledJobInRunning_PersistsWarningState()
    {
        var (sut, stateRepo, componentRepo, _, _, _) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Running));
        componentRepo.Add(BuildScheduledJob());

        await sut.RecoverAsync();

        Assert.IsTrue(stateRepo.Upserted.Any(s => s.ComponentId == "JOB-01"
            && s.Status == ComponentStatus.Warning));
    }

    [TestMethod]
    public async Task RecoverAsync_ScheduledJobInRunning_RaisesComponentStatusChangedEvent()
    {
        var (sut, stateRepo, componentRepo, _, _, events) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Running));
        componentRepo.Add(BuildScheduledJob());

        await sut.RecoverAsync();

        var changed = events.Dispatched.OfType<ComponentStatusChanged>().Single();
        Assert.AreEqual("JOB-01", changed.ComponentId);
        Assert.AreEqual(ComponentStatus.Running, changed.PreviousStatus);
        Assert.AreEqual(ComponentStatus.Warning, changed.NewStatus);
    }

    [TestMethod]
    public async Task RecoverAsync_ScheduledJobNotInRunning_NoTransitionAndNoEvent()
    {
        var (sut, stateRepo, componentRepo, cache, _, events) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Idle));
        componentRepo.Add(BuildScheduledJob());

        await sut.RecoverAsync();

        // Idle stays Idle
        Assert.AreEqual(ComponentStatus.Idle, cache.GetState("JOB-01")!.Status);
        Assert.IsEmpty(events.Dispatched.OfType<ComponentStatusChanged>());
    }

    // ── Timer registration ────────────────────────────────────────────────────

    [TestMethod]
    public async Task RecoverAsync_AllActiveComponents_RegisteredWithTimerRegistry()
    {
        var (sut, stateRepo, componentRepo, _, timers, _) = CreateSut();
        stateRepo.Seed(new ComponentState("SVC-01", ComponentStatus.Normal));
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Idle));
        componentRepo.Add(BuildService("SVC-01"));
        componentRepo.Add(BuildScheduledJob("JOB-01"));

        await sut.RecoverAsync();

        Assert.Contains("SVC-01", timers.RegisteredComponents);
        Assert.Contains("JOB-01", timers.RegisteredComponents);
    }

    [TestMethod]
    public async Task RecoverAsync_ServiceNotStopped_TimerReset()
    {
        var (sut, stateRepo, componentRepo, _, timers, _) = CreateSut();
        stateRepo.Seed(new ComponentState("SVC-01", ComponentStatus.Normal));
        componentRepo.Add(BuildService("SVC-01"));

        await sut.RecoverAsync();

        Assert.Contains("SVC-01", timers.ResetComponents);
    }

    [TestMethod]
    public async Task RecoverAsync_ServiceStopped_TimerNotReset()
    {
        var (sut, stateRepo, componentRepo, _, timers, _) = CreateSut();
        stateRepo.Seed(new ComponentState("SVC-01", ComponentStatus.Stopped));
        componentRepo.Add(BuildService("SVC-01"));

        await sut.RecoverAsync();

        Assert.DoesNotContain("SVC-01", timers.ResetComponents);
    }

    [TestMethod]
    public async Task RecoverAsync_ServiceInMaintenance_TimerNotReset()
    {
        var (sut, stateRepo, componentRepo, _, timers, _) = CreateSut();
        stateRepo.Seed(new ComponentState("SVC-01", ComponentStatus.Maintenance));
        componentRepo.Add(BuildService("SVC-01"));

        await sut.RecoverAsync();

        Assert.DoesNotContain("SVC-01", timers.ResetComponents);
    }

    [TestMethod]
    public async Task RecoverAsync_ServiceWithUnknownState_TimerReset()
    {
        // No persisted state → defaults to Unknown → timer should be armed
        var (sut, _, componentRepo, _, timers, _) = CreateSut();
        componentRepo.Add(BuildService("SVC-01"));

        await sut.RecoverAsync();

        Assert.Contains("SVC-01", timers.ResetComponents);
    }

    // ── ScheduledJob timer never reset directly ───────────────────────────────

    [TestMethod]
    public async Task RecoverAsync_ScheduledJob_TimerNeverReset()
    {
        // ScheduledJob timers are armed only when entering Running state (BI-011)
        var (sut, stateRepo, componentRepo, _, timers, _) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Idle));
        componentRepo.Add(BuildScheduledJob("JOB-01"));

        await sut.RecoverAsync();

        // Registered (Step 2) but NOT reset (Step 4 skips ScheduledJob)
        Assert.Contains("JOB-01", timers.RegisteredComponents);
        Assert.DoesNotContain("JOB-01", timers.ResetComponents);
    }

    // ── Multiple ScheduledJobs, only Running ones affected ───────────────────

    [TestMethod]
    public async Task RecoverAsync_MultipleJobsMixedStates_OnlyRunningTransitioned()
    {
        var (sut, stateRepo, componentRepo, cache, _, events) = CreateSut();
        stateRepo.Seed(new ComponentState("JOB-01", ComponentStatus.Running));
        stateRepo.Seed(new ComponentState("JOB-02", ComponentStatus.Idle));
        stateRepo.Seed(new ComponentState("JOB-03", ComponentStatus.Completed));
        componentRepo.Add(BuildScheduledJob("JOB-01"));
        componentRepo.Add(BuildScheduledJob("JOB-02"));
        componentRepo.Add(BuildScheduledJob("JOB-03"));

        await sut.RecoverAsync();

        Assert.AreEqual(ComponentStatus.Warning, cache.GetState("JOB-01")!.Status);
        Assert.AreEqual(ComponentStatus.Idle, cache.GetState("JOB-02")!.Status);
        Assert.AreEqual(ComponentStatus.Completed, cache.GetState("JOB-03")!.Status);

        Assert.HasCount(1, events.Dispatched.OfType<ComponentStatusChanged>());
    }
}
