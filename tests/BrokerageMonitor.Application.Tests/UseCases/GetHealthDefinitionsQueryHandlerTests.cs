using BrokerageMonitor.Application.DTOs;
using BrokerageMonitor.Application.UseCases.Health;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class GetHealthDef_DefinitionRepo : IHealthMonitorDefinitionRepository
{
  private readonly List<HealthMonitorDefinition> _store = [];

  public void Add(HealthMonitorDefinition def) => _store.Add(def);

  public Task<HealthMonitorDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
      => Task.FromResult(_store.FirstOrDefault(d => d.DefinitionId == id));

  public Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>([.. _store.Where(d => d.IsActive)]);

  public Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>([.. _store.Where(d => d.SystemId == systemId)]);

  public Task<IReadOnlyList<HealthMonitorDefinition>> GetByWatchedComponentAsync(string componentId, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
          [.. _store.Where(d => d.IsActive && d.WatchedComponents.Any(w => w.ComponentId == componentId))]);

  public Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default)
  {
    var idx = _store.FindIndex(d => d.DefinitionId == definition.DefinitionId);
    if (idx >= 0) _store[idx] = definition;
    else _store.Add(definition);
    return Task.CompletedTask;
  }

  public Task DeleteAsync(Guid id, CancellationToken ct = default)
  {
    _store.RemoveAll(d => d.DefinitionId == id);
    return Task.CompletedTask;
  }
}

internal sealed class GetHealthDef_ExecutionRepo : IDailyExecutionRepository
{
  private readonly List<DailyExecution> _store = [];

  public void Add(DailyExecution ex) => _store.Add(ex);

  public Task<DailyExecution?> GetByDefinitionAndDateAsync(Guid definitionId, DateOnly date, CancellationToken ct = default)
      => Task.FromResult(_store.FirstOrDefault(e => e.DefinitionId == definitionId && e.ExecutionDate == date));

  public Task<IReadOnlyList<DailyExecution>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<DailyExecution>>([.. _store.Where(e => e.ExecutionDate == date)]);

  public Task<IReadOnlyList<DailyExecution>> QueryHistoryAsync(
      Guid? definitionId, string? systemId, DateOnly from, DateOnly to, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<DailyExecution>>([]);

  public Task AddAsync(DailyExecution execution, CancellationToken ct = default)
  {
    _store.Add(execution);
    return Task.CompletedTask;
  }

  public Task UpdateStatusAsync(
      Guid executionId,
      DailyExecutionStatus status,
      DateTimeOffset evaluatedAt,
      IReadOnlyList<string>? failedComponents,
      DateTimeOffset? notificationSentAt,
      CancellationToken ct = default)
      => Task.CompletedTask;

  public Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default)
      => Task.CompletedTask;

  public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
      => Task.CompletedTask;
}

internal sealed class GetHealthDef_ComponentRepo : IMonitoredComponentRepository
{
  private readonly List<MonitoredComponent> _store = [];

  public void Add(MonitoredComponent component) => _store.Add(component);

  public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
      => Task.FromResult(_store.FirstOrDefault(c => c.ComponentId == componentId));

  public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<MonitoredComponent>>([.. _store.Where(c => c.SystemId == systemId)]);

  public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<MonitoredComponent>>([.. _store.Where(c => c.IsActive)]);

  public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
  {
    _store.Add(component);
    return Task.CompletedTask;
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

file static class HealthDefBuilder
{
  internal static HealthMonitorDefinition MakeDefinition(
      Guid? id = null,
      string systemId = "SYS-01",
      string componentId = "COMP-01",
      bool isActive = true)
  {
    var definitionId = id ?? Guid.NewGuid();
    return new HealthMonitorDefinition(
        definitionId,
        systemId,
        "Daily Health Check",
        new TimeOnly(18, 0),
        new HealthRuleSchedule(ScheduleType.Daily),
        [new WatchedComponent(componentId, ComponentType.Service)],
        isActive: isActive);
  }

  internal static DailyExecution MakeExecution(Guid definitionId, string systemId = "SYS-01")
      => new(
          Guid.NewGuid(),
          definitionId,
          systemId,
          DateOnly.FromDateTime(DateTime.Today),
          DailyExecutionStatus.InProgress);

  internal static MonitoredComponent MakeComponent(string componentId = "COMP-01", string systemId = "SYS-01")
      => new(componentId, systemId, "Service A", ComponentType.Service, "topic.a", 30);
}

// ---------------------------------------------------------------------------
// GetHealthDefinitionsQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetHealthDefinitionsQueryHandlerTests
{
  private readonly GetHealthDef_DefinitionRepo _definitionRepo = new();
  private readonly GetHealthDef_ExecutionRepo _executionRepo = new();
  private readonly GetHealthDef_ComponentRepo _componentRepo = new();
  private readonly GetHealthDefinitionsQueryHandler _sut;

  public GetHealthDefinitionsQueryHandlerTests()
  {
    _sut = new GetHealthDefinitionsQueryHandler(
        _definitionRepo,
        _executionRepo,
        _componentRepo,
        NullLogger<GetHealthDefinitionsQueryHandler>.Instance);
  }

  // -----------------------------------------------------------------------
  // HandleAsync — empty repository
  // -----------------------------------------------------------------------

  [TestMethod]
  public async Task HandleAsync_NoDefinitions_ReturnsEmptyList()
  {
    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    Assert.IsEmpty(result);
  }

  // -----------------------------------------------------------------------
  // HandleAsync — null SystemId queries all active definitions
  // -----------------------------------------------------------------------

  [TestMethod]
  public async Task HandleAsync_NullSystemId_ReturnsAllActiveDefinitions()
  {
    // Arrange
    var def1 = HealthDefBuilder.MakeDefinition(systemId: "SYS-01");
    var def2 = HealthDefBuilder.MakeDefinition(systemId: "SYS-02");
    _definitionRepo.Add(def1);
    _definitionRepo.Add(def2);

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    Assert.HasCount(2, result);
  }

  [TestMethod]
  public async Task HandleAsync_NullSystemId_ExcludesInactiveDefinitions()
  {
    // Arrange
    var active = HealthDefBuilder.MakeDefinition(systemId: "SYS-01", isActive: true);
    var inactive = HealthDefBuilder.MakeDefinition(systemId: "SYS-02", isActive: false);
    _definitionRepo.Add(active);
    _definitionRepo.Add(inactive);

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    Assert.HasCount(1, result);
    Assert.AreEqual(active.DefinitionId, result[0].DefinitionId);
  }

  // -----------------------------------------------------------------------
  // HandleAsync — specific SystemId filters by system
  // -----------------------------------------------------------------------

  [TestMethod]
  public async Task HandleAsync_WithSystemId_ReturnsOnlyMatchingDefinitions()
  {
    // Arrange
    var def1 = HealthDefBuilder.MakeDefinition(systemId: "SYS-01");
    var def2 = HealthDefBuilder.MakeDefinition(systemId: "SYS-02");
    _definitionRepo.Add(def1);
    _definitionRepo.Add(def2);

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery("SYS-01"));

    // Assert
    Assert.HasCount(1, result);
    Assert.AreEqual("SYS-01", result[0].SystemId);
  }

  [TestMethod]
  public async Task HandleAsync_WithSystemId_NoMatch_ReturnsEmptyList()
  {
    // Arrange
    _definitionRepo.Add(HealthDefBuilder.MakeDefinition(systemId: "SYS-01"));

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery("SYS-UNKNOWN"));

    // Assert
    Assert.IsEmpty(result);
  }

  // -----------------------------------------------------------------------
  // HandleAsync — DTO mapping
  // -----------------------------------------------------------------------

  [TestMethod]
  public async Task HandleAsync_SingleDefinition_MapsAllPropertiesCorrectly()
  {
    // Arrange
    var def = HealthDefBuilder.MakeDefinition();
    _definitionRepo.Add(def);

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    var dto = result[0];
    Assert.AreEqual(def.DefinitionId, dto.DefinitionId);
    Assert.AreEqual(def.SystemId, dto.SystemId);
    Assert.AreEqual(def.Name, dto.Name);
    Assert.AreEqual(def.DeadlineTime, dto.DeadlineTime);
    Assert.AreEqual(def.Schedule.ScheduleType, dto.ScheduleType);
    Assert.AreEqual(def.SendOnFailure, dto.SendOnFailure);
    Assert.AreEqual(def.IsActive, dto.IsActive);
  }

  [TestMethod]
  public async Task HandleAsync_DefinitionWithNoTodayExecution_TodayExecutionIsNull()
  {
    // Arrange
    _definitionRepo.Add(HealthDefBuilder.MakeDefinition());
    // No executions seeded for today

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    Assert.IsNull(result[0].TodayExecution);
  }

  [TestMethod]
  public async Task HandleAsync_DefinitionWithTodayExecution_TodayExecutionIsMapped()
  {
    // Arrange
    var def = HealthDefBuilder.MakeDefinition();
    var execution = HealthDefBuilder.MakeExecution(def.DefinitionId);
    _definitionRepo.Add(def);
    _executionRepo.Add(execution);

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    var dto = result[0];
    Assert.IsNotNull(dto.TodayExecution);
    Assert.AreEqual(execution.ExecutionId, dto.TodayExecution.ExecutionId);
    Assert.AreEqual(execution.DefinitionId, dto.TodayExecution.DefinitionId);
    Assert.AreEqual(execution.Status, dto.TodayExecution.Status);
  }

  // -----------------------------------------------------------------------
  // HandleAsync — WatchedComponents name resolution
  // -----------------------------------------------------------------------

  [TestMethod]
  public async Task HandleAsync_ComponentExists_ResolvesComponentName()
  {
    // Arrange
    var component = HealthDefBuilder.MakeComponent("COMP-01");
    _componentRepo.Add(component);
    _definitionRepo.Add(HealthDefBuilder.MakeDefinition(componentId: "COMP-01"));

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert
    var watchedDto = result[0].WatchedComponents[0];
    Assert.AreEqual("Service A", watchedDto.ComponentName);
    Assert.AreEqual("COMP-01", watchedDto.ComponentId);
  }

  [TestMethod]
  public async Task HandleAsync_ComponentNotInRepo_FallsBackToComponentId()
  {
    // Arrange — no component in repo
    _definitionRepo.Add(HealthDefBuilder.MakeDefinition(componentId: "COMP-UNKNOWN"));

    // Act
    var result = await _sut.HandleAsync(new GetHealthDefinitionsQuery());

    // Assert — falls back to ComponentId as the name
    var watchedDto = result[0].WatchedComponents[0];
    Assert.AreEqual("COMP-UNKNOWN", watchedDto.ComponentName);
  }
}
// NOTE: CancellationToken propagation to repository methods is validated at the
// infrastructure integration level (real Dapper queries honour cancellation).
// In-memory fakes used here do not throw on a pre-cancelled token, so that
// branch is intentionally not tested in this unit-test class.
