using BrokerageMonitor.Application.UseCases.Management;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles (prefixed UpsertSys_ to avoid cross-file conflicts)
// ---------------------------------------------------------------------------

internal sealed class UpsertSys_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = new();

    public void Seed(MonitoredSystem system) => _store[system.SystemId] = system;

    public MonitoredSystem? GetStored(string id) => _store.GetValueOrDefault(id);

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        _store[system.SystemId] = system;
        return Task.CompletedTask;
    }

}

// ---------------------------------------------------------------------------
// UpsertMonitoredSystemHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class UpsertMonitoredSystemHandlerTests
{
    private readonly UpsertSys_SystemRepo _repo = new();
    private readonly UpsertMonitoredSystemHandler _sut;

    public UpsertMonitoredSystemHandlerTests()
    {
        _sut = new UpsertMonitoredSystemHandler(_repo, NullLogger<UpsertMonitoredSystemHandler>.Instance);
    }

    // ── HandleAsync — null guard ──────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.HandleAsync(null!));
    }

    // ── Create path ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NewSystemId_CreatesSystem()
    {
        // Arrange
        var cmd = new UpsertMonitoredSystemCommand(
            "SYS-A", "Alpha", new TimeOnly(9, 0), new TimeOnly(13, 30), [], true);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        var stored = _repo.GetStored("SYS-A");
        Assert.IsNotNull(stored);
        Assert.AreEqual("Alpha", stored.Name);
    }

    [TestMethod]
    public async Task HandleAsync_NewSystemWithRecipients_StoresEmailAddresses()
    {
        // Arrange
        var cmd = new UpsertMonitoredSystemCommand(
            "SYS-B", "Beta", new TimeOnly(9, 0), new TimeOnly(13, 30),
            ["ops@example.com", "dev@example.com"]);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        var stored = _repo.GetStored("SYS-B")!;
        Assert.HasCount(2, stored.AlertRecipients);
    }

    // ── Update path ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_ExistingSystemId_UpdatesNameAndMarketSession()
    {
        // Arrange
        var existing = new MonitoredSystem(
            "SYS-C", "Old Name", new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(13, 30)));
        _repo.Seed(existing);

        var cmd = new UpsertMonitoredSystemCommand(
            "SYS-C", "New Name", new TimeOnly(8, 0), new TimeOnly(14, 0), []);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        var stored = _repo.GetStored("SYS-C")!;
        Assert.AreEqual("New Name", stored.Name);
        Assert.AreEqual(new TimeOnly(8, 0), stored.MarketSession.StartTime);
    }

    [TestMethod]
    public async Task HandleAsync_InvalidMarketSession_ThrowsArgumentException()
    {
        // Arrange — start >= end is invalid
        var cmd = new UpsertMonitoredSystemCommand(
            "SYS-D", "Delta", new TimeOnly(14, 0), new TimeOnly(9, 0), []);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(cmd));
    }
}

// ---------------------------------------------------------------------------
// Test doubles for component handler
// ---------------------------------------------------------------------------

internal sealed class UpsertComp_ComponentRepo : IMonitoredComponentRepository
{
    private readonly Dictionary<string, MonitoredComponent> _store = new();

    public void Seed(MonitoredComponent comp) => _store[comp.ComponentId] = comp;

    public MonitoredComponent? GetStored(string id) => _store.GetValueOrDefault(id);

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
            _store.Values.Where(c => c.SystemId == systemId).ToList());

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
    {
        _store[component.ComponentId] = component;
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// UpsertMonitoredComponentHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class UpsertMonitoredComponentHandlerTests
{
    private readonly UpsertComp_ComponentRepo _repo = new();
    private readonly UpsertMonitoredComponentHandler _sut;

    public UpsertMonitoredComponentHandlerTests()
    {
        _sut = new UpsertMonitoredComponentHandler(_repo, NullLogger<UpsertMonitoredComponentHandler>.Instance);
    }

    // ── HandleAsync — null guard ──────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.HandleAsync(null!));
    }

    // ── Create path ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NewComponentId_CreatesComponent()
    {
        // Arrange
        var cmd = new UpsertMonitoredComponentCommand(
            "COMP-A", "SYS-A", "Alpha Service",
            ComponentType.Service, "topic.alpha", 30);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        var stored = _repo.GetStored("COMP-A");
        Assert.IsNotNull(stored);
        Assert.AreEqual("Alpha Service", stored.Name);
        Assert.AreEqual(ComponentType.Service, stored.ComponentType);
    }

    [TestMethod]
    public async Task HandleAsync_ScheduledJobWithoutCron_ThrowsArgumentException()
    {
        // Arrange — CronExpression is null for a ScheduledJob (BI required)
        var cmd = new UpsertMonitoredComponentCommand(
            "COMP-B", "SYS-A", "Job",
            ComponentType.ScheduledJob, "topic.job", 60, null);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.HandleAsync(cmd));
    }

    // ── Update path ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_ExistingComponentId_UpdatesName()
    {
        // Arrange
        var existing = new MonitoredComponent(
            "COMP-C", "SYS-A", "Old Name", ComponentType.Service, "topic.c", 30);
        _repo.Seed(existing);

        var cmd = new UpsertMonitoredComponentCommand(
            "COMP-C", "SYS-A", "New Name", ComponentType.Service, "topic.c", 45);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        var stored = _repo.GetStored("COMP-C")!;
        Assert.AreEqual("New Name", stored.Name);
        Assert.AreEqual(45, stored.HeartbeatTimeoutSeconds);
    }

    [TestMethod]
    public async Task HandleAsync_DeactivateComponent_SetsIsActiveFalse()
    {
        // Arrange
        var existing = new MonitoredComponent(
            "COMP-D", "SYS-A", "Svc D", ComponentType.Service, "topic.d", 30);
        _repo.Seed(existing);

        var cmd = new UpsertMonitoredComponentCommand(
            "COMP-D", "SYS-A", "Svc D", ComponentType.Service, "topic.d", 30, IsActive: false);

        // Act
        await _sut.HandleAsync(cmd);

        // Assert
        Assert.IsFalse(_repo.GetStored("COMP-D")!.IsActive);
    }
}
