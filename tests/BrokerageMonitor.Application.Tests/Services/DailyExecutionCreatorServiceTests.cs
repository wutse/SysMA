using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

// ---------------------------------------------------------------------------
// Test doubles (prefixed DEC_ to avoid cross-file naming conflicts)
// ---------------------------------------------------------------------------

internal sealed class DEC_DefinitionRepo : IHealthMonitorDefinitionRepository
{
    private readonly List<HealthMonitorDefinition> _store = [];

    public void Add(HealthMonitorDefinition def) => _store.Add(def);

    public Task<HealthMonitorDefinition?> GetByIdAsync(Guid definitionId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(d => d.DefinitionId == definitionId));

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>([.. _store.Where(d => d.IsActive)]);

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
            [.. _store.Where(d => d.SystemId == systemId)]);

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetByWatchedComponentAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
            [.. _store.Where(d => d.IsActive && d.WatchedComponents.Any(w => w.ComponentId == componentId))]);

    public Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default)
    {
        _store.RemoveAll(d => d.DefinitionId == definition.DefinitionId);
        _store.Add(definition);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid definitionId, CancellationToken ct = default)
    {
        _store.RemoveAll(d => d.DefinitionId == definitionId);
        return Task.CompletedTask;
    }
}

internal sealed class DEC_ExecutionRepo : IDailyExecutionRepository
{
    private readonly List<DailyExecution> _store = [];

    public List<DailyExecution> Store => _store;

    public void Add(DailyExecution execution) => _store.Add(execution);

    public Task<DailyExecution?> GetByDefinitionAndDateAsync(
        Guid definitionId, DateOnly date, CancellationToken ct = default)
        => Task.FromResult(
            _store.FirstOrDefault(e => e.DefinitionId == definitionId && e.ExecutionDate == date));

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
        CancellationToken ct = default) => Task.CompletedTask;

    public Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class DEC_ComponentStateRepo : IComponentStateRepository
{
    public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult<ComponentState?>(null);

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> componentIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([]);

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class DEC_StateCache : IComponentStateCache
{
    private readonly Dictionary<string, ComponentState> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Seed(ComponentState state) => _cache[state.ComponentId] = state;

    public ComponentState? GetState(string componentId)
        => _cache.GetValueOrDefault(componentId);

    public void SetState(ComponentState state) => _cache[state.ComponentId] = state;

    public IReadOnlyList<ComponentState> GetAllStates() => [.. _cache.Values];

    public void LoadAll(IEnumerable<ComponentState> states)
    {
        _cache.Clear();
        foreach (var s in states) _cache[s.ComponentId] = s;
    }
}

internal sealed class DEC_Realtime : IRealtimeNotificationService
{
    public List<Guid> NotifiedExecutionIds { get; } = [];

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
    {
        NotifiedExecutionIds.Add(executionId);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class DailyExecutionCreatorServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static HealthMonitorDefinition MakeDailyDefinition(
        Guid? definitionId = null,
        string systemId = "SYS",
        string componentId = "COMP-001",
        TimeOnly? deadlineTime = null)
    {
        var id = definitionId ?? Guid.NewGuid();
        var schedule = new HealthRuleSchedule(ScheduleType.Daily);
        return new HealthMonitorDefinition(
            definitionId: id,
            systemId: systemId,
            name: "Test Definition",
            deadlineTime: deadlineTime ?? new TimeOnly(18, 0),
            schedule: schedule,
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
    }

    private static (
        DailyExecutionCreatorService Sut,
        DEC_DefinitionRepo Definitions,
        DEC_ExecutionRepo Executions,
        DEC_StateCache Cache,
        DEC_Realtime Realtime) BuildSut()
    {
        var definitions = new DEC_DefinitionRepo();
        var executions = new DEC_ExecutionRepo();
        var componentStateRepo = new DEC_ComponentStateRepo();
        var cache = new DEC_StateCache();
        var realtime = new DEC_Realtime();

        var sut = new DailyExecutionCreatorService(
            definitions,
            executions,
            componentStateRepo,
            cache,
            realtime,
            NullLogger<DailyExecutionCreatorService>.Instance);

        return (sut, definitions, executions, cache, realtime);
    }

    // ── CreateForDateAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateForDateAsync_DailyDefinitionMatchesDate_CreatesExecution()
    {
        // Arrange
        var (sut, definitions, executions, _, realtime) = BuildSut();
        var def = MakeDailyDefinition();
        definitions.Add(def);
        var targetDate = new DateOnly(2026, 1, 15); // Wednesday — matches Daily

        // Act
        await sut.CreateForDateAsync(targetDate);

        // Assert
        Assert.HasCount(1, executions.Store);
        var exe = executions.Store[0];
        Assert.AreEqual(def.DefinitionId, exe.DefinitionId);
        Assert.AreEqual(targetDate, exe.ExecutionDate);
        Assert.AreEqual(DailyExecutionStatus.InProgress, exe.Status);
    }

    [TestMethod]
    public async Task CreateForDateAsync_BI015_WeeklyScheduleMismatch_SkipsCreation()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();

        // Weekly schedule for Monday, but target date is Tuesday
        var schedule = new HealthRuleSchedule(ScheduleType.Weekly, null, DayOfWeek.Monday);
        var def = new HealthMonitorDefinition(
            definitionId: Guid.NewGuid(),
            systemId: "SYS",
            name: "Weekly Monday",
            deadlineTime: new TimeOnly(18, 0),
            schedule: schedule,
            watchedComponents: [new WatchedComponent("COMP-001", ComponentType.ScheduledJob)]);
        definitions.Add(def);

        var tuesday = new DateOnly(2026, 1, 13); // Tuesday

        // Act
        await sut.CreateForDateAsync(tuesday);

        // Assert — BI-015: no execution created
        Assert.IsEmpty(executions.Store);
    }

    [TestMethod]
    public async Task CreateForDateAsync_BI012_ExecutionAlreadyExists_SkipsCreation()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();
        var def = MakeDailyDefinition();
        definitions.Add(def);

        var date = new DateOnly(2026, 1, 15);
        var existing = new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: def.DefinitionId,
            systemId: "SYS",
            executionDate: date);
        executions.Add(existing);

        // Act
        await sut.CreateForDateAsync(date);

        // Assert — BI-012: still only one execution
        Assert.HasCount(1, executions.Store);
    }

    [TestMethod]
    public async Task CreateForDateAsync_ScheduledJobComponent_ResetsStateToIdle()
    {
        // Arrange
        var (sut, definitions, executions, cache, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDailyDefinition(componentId: componentId);
        definitions.Add(def);

        // Seed a non-idle state in cache
        var state = new ComponentState(componentId, ComponentStatus.Completed);
        cache.Seed(state);

        // Act
        await sut.CreateForDateAsync(new DateOnly(2026, 1, 15));

        // Assert — component state reset to Idle
        var cachedState = cache.GetState(componentId);
        Assert.IsNotNull(cachedState);
        Assert.AreEqual(ComponentStatus.Idle, cachedState.Status);
    }

    [TestMethod]
    public async Task CreateForDateAsync_MultipleDefinitions_CreatesOnePerMatchingDefinition()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();
        var monday = new DateOnly(2026, 1, 12); // Monday

        var dailyDef = MakeDailyDefinition();
        var weeklyMonday = new HealthMonitorDefinition(
            definitionId: Guid.NewGuid(),
            systemId: "SYS",
            name: "Weekly Monday",
            deadlineTime: new TimeOnly(18, 0),
            schedule: new HealthRuleSchedule(ScheduleType.Weekly, null, DayOfWeek.Monday),
            watchedComponents: [new WatchedComponent("COMP-002", ComponentType.Service)]);
        var weeklyFriday = new HealthMonitorDefinition(
            definitionId: Guid.NewGuid(),
            systemId: "SYS",
            name: "Weekly Friday",
            deadlineTime: new TimeOnly(18, 0),
            schedule: new HealthRuleSchedule(ScheduleType.Weekly, null, DayOfWeek.Friday),
            watchedComponents: [new WatchedComponent("COMP-003", ComponentType.Service)]);

        definitions.Add(dailyDef);
        definitions.Add(weeklyMonday);
        definitions.Add(weeklyFriday);

        // Act
        await sut.CreateForDateAsync(monday);

        // Assert — Daily + Weekly Monday match; Weekly Friday does not
        Assert.HasCount(2, executions.Store);
    }

    [TestMethod]
    public async Task CreateForDateAsync_CreatesExecution_PushesRealtimeNotification()
    {
        // Arrange
        var (sut, definitions, executions, _, realtime) = BuildSut();
        var def = MakeDailyDefinition();
        definitions.Add(def);

        // Act
        await sut.CreateForDateAsync(new DateOnly(2026, 1, 15));

        // Assert
        Assert.HasCount(1, realtime.NotifiedExecutionIds);
        Assert.AreEqual(executions.Store[0].ExecutionId, realtime.NotifiedExecutionIds[0]);
    }

    // ── RecoverTodayAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task RecoverTodayAsync_BI012_ExecutionAlreadyExistsForToday_SkipsCreation()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();
        var def = MakeDailyDefinition();
        definitions.Add(def);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var existing = new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: def.DefinitionId,
            systemId: "SYS",
            executionDate: today);
        executions.Add(existing);

        // Act
        await sut.RecoverTodayAsync();

        // Assert — BI-012: no duplicate
        Assert.HasCount(1, executions.Store);
    }

    [TestMethod]
    public async Task RecoverTodayAsync_BI015_ScheduleMismatch_SkipsCreation()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();

        // A weekly schedule that doesn't match today
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var tomorrowDow = tomorrow.DayOfWeek;

        // If today is Sunday (0), tomorrowDow will be Monday (1) — guaranteed mismatch
        // Use the next day-of-week so it never matches today
        var schedule = new HealthRuleSchedule(ScheduleType.Weekly, null, tomorrowDow);
        var def = new HealthMonitorDefinition(
            definitionId: Guid.NewGuid(),
            systemId: "SYS",
            name: "Mismatch",
            deadlineTime: new TimeOnly(18, 0),
            schedule: schedule,
            watchedComponents: [new WatchedComponent("COMP-001", ComponentType.Service)]);
        definitions.Add(def);

        // Act
        await sut.RecoverTodayAsync();

        // Assert
        Assert.IsEmpty(executions.Store);
    }

    [TestMethod]
    public async Task RecoverTodayAsync_NoExistingExecution_CreatesExecutionForToday()
    {
        // Arrange
        var (sut, definitions, executions, _, _) = BuildSut();
        var def = MakeDailyDefinition(deadlineTime: new TimeOnly(23, 59)); // deadline far in future
        definitions.Add(def);

        // Act
        await sut.RecoverTodayAsync();

        // Assert
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.HasCount(1, executions.Store);
        Assert.AreEqual(today, executions.Store[0].ExecutionDate);
    }
}
