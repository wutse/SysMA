using BrokerageMonitor.Application.UseCases.Management;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class MgmtQuery_SystemRepo : IMonitoredSystemRepository
{
  private readonly List<MonitoredSystem> _systems = [];

  public void Add(MonitoredSystem system) => _systems.Add(system);

  public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
      => Task.FromResult(_systems.FirstOrDefault(s => s.SystemId == systemId));

  public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _systems.Where(s => s.IsActive)]);

  public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
      => Task.CompletedTask;
}

internal sealed class MgmtQuery_ComponentRepo : IMonitoredComponentRepository
{
  private readonly List<MonitoredComponent> _components = [];

  public void Add(MonitoredComponent component) => _components.Add(component);

  public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
      => Task.FromResult(_components.FirstOrDefault(c => c.ComponentId == componentId));

  public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
          [.. _components.Where(c => c.SystemId == systemId)]);

  public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<MonitoredComponent>>(
          [.. _components.Where(c => c.IsActive)]);

  public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
      => Task.CompletedTask;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class MgmtFactory
{
  private static readonly MarketSessionWindow Session =
      new(new TimeOnly(9, 0), new TimeOnly(17, 0));

  public static MonitoredSystem System(string id, bool active = true) =>
      new(id, $"System {id}", Session, isActive: active);

  public static MonitoredComponent Component(string id, string systemId, bool active = true) =>
      new(id, systemId, id, ComponentType.Service, $"TOPIC/{id}", 30, isActive: active);
}

// ---------------------------------------------------------------------------
// GetActiveSystemIdsQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetActiveSystemIdsQueryHandlerTests
{
  [TestMethod]
  public async Task HandleAsync_ReturnsOnlyActiveSystemIds()
  {
    // Arrange
    var repo = new MgmtQuery_SystemRepo();
    repo.Add(MgmtFactory.System("SYS-A", active: true));
    repo.Add(MgmtFactory.System("SYS-B", active: false));
    var sut = new GetActiveSystemIdsQueryHandler(repo);

    // Act
    var result = await sut.HandleAsync();

    // Assert
    Assert.HasCount(1, result);
    Assert.AreEqual("SYS-A", result[0]);
  }

  [TestMethod]
  public async Task HandleAsync_ResultIsOrderedAlphabetically()
  {
    // Arrange
    var repo = new MgmtQuery_SystemRepo();
    repo.Add(MgmtFactory.System("SYS-C"));
    repo.Add(MgmtFactory.System("SYS-A"));
    repo.Add(MgmtFactory.System("SYS-B"));
    var sut = new GetActiveSystemIdsQueryHandler(repo);

    // Act
    var result = await sut.HandleAsync();

    // Assert
    Assert.AreEqual("SYS-A", result[0]);
    Assert.AreEqual("SYS-B", result[1]);
    Assert.AreEqual("SYS-C", result[2]);
  }

  [TestMethod]
  public async Task HandleAsync_NoActiveSystems_ReturnsEmpty()
  {
    // Arrange
    var repo = new MgmtQuery_SystemRepo();
    var sut = new GetActiveSystemIdsQueryHandler(repo);

    // Act
    var result = await sut.HandleAsync();

    // Assert
    Assert.IsEmpty(result);
  }
}

// ---------------------------------------------------------------------------
// GetSystemsWithComponentsQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetSystemsWithComponentsQueryHandlerTests
{
  [TestMethod]
  public async Task HandleAsync_ReturnsSystems_WithMatchingComponents()
  {
    // Arrange
    var systemRepo = new MgmtQuery_SystemRepo();
    systemRepo.Add(MgmtFactory.System("SYS-01"));

    var compRepo = new MgmtQuery_ComponentRepo();
    compRepo.Add(MgmtFactory.Component("COMP-A", "SYS-01"));
    compRepo.Add(MgmtFactory.Component("COMP-B", "SYS-01"));

    var sut = new GetSystemsWithComponentsQueryHandler(systemRepo, compRepo);

    // Act
    var (systems, bySystem) = await sut.HandleAsync();

    // Assert
    Assert.HasCount(1, systems);
    Assert.IsTrue(bySystem.ContainsKey("SYS-01"));
    Assert.HasCount(2, bySystem["SYS-01"]);
  }

  [TestMethod]
  public async Task HandleAsync_SystemsOrderedBySystemId()
  {
    // Arrange
    var systemRepo = new MgmtQuery_SystemRepo();
    systemRepo.Add(MgmtFactory.System("SYS-C"));
    systemRepo.Add(MgmtFactory.System("SYS-A"));
    var compRepo = new MgmtQuery_ComponentRepo();
    var sut = new GetSystemsWithComponentsQueryHandler(systemRepo, compRepo);

    // Act
    var (systems, _) = await sut.HandleAsync();

    // Assert
    Assert.AreEqual("SYS-A", systems[0].SystemId);
    Assert.AreEqual("SYS-C", systems[1].SystemId);
  }
}

// ---------------------------------------------------------------------------
// GetActiveComponentsQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetActiveComponentsQueryHandlerTests
{
  [TestMethod]
  public async Task HandleAsync_ReturnsOnlyActiveComponents()
  {
    // Arrange
    var repo = new MgmtQuery_ComponentRepo();
    repo.Add(MgmtFactory.Component("COMP-01", "SYS-A", active: true));
    repo.Add(MgmtFactory.Component("COMP-02", "SYS-A", active: false));
    var sut = new GetActiveComponentsQueryHandler(repo);

    // Act
    var result = await sut.HandleAsync();

    // Assert
    Assert.HasCount(1, result);
    Assert.AreEqual("COMP-01", result[0].ComponentId);
  }

  [TestMethod]
  public async Task HandleAsync_ComponentsOrderedBySystemThenName()
  {
    // Arrange
    var repo = new MgmtQuery_ComponentRepo();
    repo.Add(MgmtFactory.Component("COMP-Z", "SYS-B"));
    repo.Add(MgmtFactory.Component("COMP-A", "SYS-B"));
    repo.Add(MgmtFactory.Component("COMP-M", "SYS-A"));
    var sut = new GetActiveComponentsQueryHandler(repo);

    // Act
    var result = await sut.HandleAsync();

    // Assert — SYS-A components before SYS-B, then by name within each system
    Assert.AreEqual("SYS-A", result[0].SystemId);
    Assert.AreEqual("COMP-A", result[1].ComponentId);
    Assert.AreEqual("COMP-Z", result[2].ComponentId);
  }
}
