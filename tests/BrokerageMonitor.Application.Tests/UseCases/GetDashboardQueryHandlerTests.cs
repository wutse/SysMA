using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.UseCases.Dashboard;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Fake repositories
// ---------------------------------------------------------------------------

internal sealed class FakeSystemRepository : IMonitoredSystemRepository
{
    private readonly List<MonitoredSystem> _systems = [];

    public void Add(MonitoredSystem s) => _systems.Add(s);

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_systems.FirstOrDefault(s => s.SystemId == systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>(_systems.Where(s => s.IsActive).ToList());

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        _systems.RemoveAll(s => s.SystemId == system.SystemId);
        _systems.Add(system);
        return Task.CompletedTask;
    }

    public Task SetMaintenanceModeAsync(string systemId, bool active, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakeDashboardComponentRepository : IMonitoredComponentRepository
{
    private readonly List<MonitoredComponent> _components = [];

    public void Add(MonitoredComponent c) => _components.Add(c);

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_components.FirstOrDefault(c => c.ComponentId == componentId));

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

internal sealed class FakeDashboardAlertRepository : IAlertRecordRepository
{
    private readonly HashSet<string> _systemsWithAlerts = [];

    public void SetAlert(string systemId) => _systemsWithAlerts.Add(systemId);

    public Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_systemsWithAlerts.Contains(systemId));

    public Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>([]);

    public Task AddAsync(AlertRecord alert, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>([]);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetDashboardQueryHandlerTests
{
    private static readonly MarketSessionWindow DefaultSession =
        new(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)),
            TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)));

    private static MonitoredSystem BuildSystem(
        string systemId = "SYS-01",
        bool isMaintenanceActive = false,
        bool isActive = true)
    {
        var sys = new MonitoredSystem(systemId, $"System {systemId}", DefaultSession, isActive: isActive);
        if (isMaintenanceActive) sys.ActivateMaintenance("test-op");
        return sys;
    }

    private static MonitoredComponent BuildComponent(
        string componentId = "COMP-01",
        string systemId = "SYS-01",
        bool isActive = true) =>
        new(componentId, systemId, $"Component {componentId}",
            ComponentType.Service, "topic", 30, isActive: isActive);

    private (GetDashboardQueryHandler sut,
             FakeSystemRepository systems,
             FakeDashboardComponentRepository components,
             ComponentStateCache cache,
             FakeDashboardAlertRepository alerts) CreateSut()
    {
        var systems = new FakeSystemRepository();
        var components = new FakeDashboardComponentRepository();
        var cache = new ComponentStateCache();
        var alerts = new FakeDashboardAlertRepository();
        var rollup = new StateRollupService();
        var sut = new GetDashboardQueryHandler(systems, components, alerts, cache, rollup);
        return (sut, systems, components, cache, alerts);
    }

    // ── Empty result ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NoActiveSystems_ReturnsEmptyList()
    {
        var (sut, _, _, _, _) = CreateSut();

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.IsEmpty(result);
    }

    // ── Single system ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_OneSystem_ReturnsOneDto()
    {
        var (sut, systems, _, _, _) = CreateSut();
        systems.Add(BuildSystem("SYS-01"));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.ContainsSingle(result);
        Assert.AreEqual("SYS-01", result[0].SystemId);
    }

    [TestMethod]
    public async Task HandleAsync_ComponentWithCachedState_ReflectsStateInDto()
    {
        var (sut, systems, components, cache, _) = CreateSut();
        systems.Add(BuildSystem());
        components.Add(BuildComponent());

        var state = new ComponentState("COMP-01", ComponentStatus.Warning);
        cache.SetState(state);

        var result = await sut.HandleAsync(new GetDashboardQuery());

        var compDto = result[0].Components.Single();
        Assert.AreEqual(ComponentStatus.Warning, compDto.Status);
    }

    [TestMethod]
    public async Task HandleAsync_ComponentWithNoCachedState_DefaultsToUnknown()
    {
        var (sut, systems, components, _, _) = CreateSut();
        systems.Add(BuildSystem());
        components.Add(BuildComponent());

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.AreEqual(ComponentStatus.Unknown, result[0].Components[0].Status);
    }

    // ── RolledUpStatus ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_MultipleComponents_RolledUpStatusIsWorstCase()
    {
        var (sut, systems, components, cache, _) = CreateSut();
        systems.Add(BuildSystem());
        components.Add(BuildComponent("C1"));
        components.Add(BuildComponent("C2"));
        components.Add(BuildComponent("C3"));

        cache.SetState(new ComponentState("C1", ComponentStatus.Normal));
        cache.SetState(new ComponentState("C2", ComponentStatus.Warning));
        cache.SetState(new ComponentState("C3", ComponentStatus.Error));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.AreEqual(ComponentStatus.Error, result[0].RolledUpStatus);
    }

    // ── HasUnacknowledgedAlert ───────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_SystemHasAlert_HasUnacknowledgedAlertIsTrue()
    {
        var (sut, systems, _, _, alerts) = CreateSut();
        systems.Add(BuildSystem("SYS-01"));
        alerts.SetAlert("SYS-01");

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.IsTrue(result[0].HasUnacknowledgedAlert);
    }

    [TestMethod]
    public async Task HandleAsync_NoAlert_HasUnacknowledgedAlertIsFalse()
    {
        var (sut, systems, _, _, _) = CreateSut();
        systems.Add(BuildSystem("SYS-01"));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.IsFalse(result[0].HasUnacknowledgedAlert);
    }

    // ── Maintenance mode ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_MaintenanceActive_IsMaintenanceActiveIsTrue()
    {
        var (sut, systems, _, _, _) = CreateSut();
        systems.Add(BuildSystem(isMaintenanceActive: true));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.IsTrue(result[0].IsMaintenanceActive);
    }

    // ── Inactive components excluded ─────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_InactiveComponent_ExcludedFromComponentDtos()
    {
        var (sut, systems, components, _, _) = CreateSut();
        systems.Add(BuildSystem());
        components.Add(BuildComponent("ACTIVE-01", isActive: true));
        components.Add(BuildComponent("INACTIVE-01", isActive: false));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.ContainsSingle(result[0].Components);
        Assert.AreEqual("ACTIVE-01", result[0].Components[0].ComponentId);
    }

    // ── Multiple systems ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_MultipleSystems_ReturnsDtoForEach()
    {
        var (sut, systems, _, _, _) = CreateSut();
        systems.Add(BuildSystem("SYS-01"));
        systems.Add(BuildSystem("SYS-02"));
        systems.Add(BuildSystem("SYS-03"));

        var result = await sut.HandleAsync(new GetDashboardQuery());

        Assert.HasCount(3, result);
    }

    // ── Null guard ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullQuery_ThrowsArgumentNullException()
    {
        var (sut, _, _, _, _) = CreateSut();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => sut.HandleAsync(null!));
    }
}
