using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.UseCases.Health;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class HealthDef_DefinitionRepo : IHealthMonitorDefinitionRepository
{
    private readonly Dictionary<Guid, HealthMonitorDefinition> _store = new();

    public HealthMonitorDefinition? GetStored(Guid id) => _store.GetValueOrDefault(id);

    public Task<HealthMonitorDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>([.. _store.Values.Where(d => d.SystemId == systemId)]);

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetByWatchedComponentAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
            [.. _store.Values.Where(d => d.IsActive && d.WatchedComponents.Any(w => w.ComponentId == componentId))]);

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>([.. _store.Values]);

    public Task UpsertAsync(HealthMonitorDefinition definition, CancellationToken ct = default)
    {
        _store[definition.DefinitionId] = definition;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}

internal sealed class SpyHealthJobScheduler : IHealthJobScheduler
{
    public List<(Guid DefinitionId, TimeOnly DeadlineTime)> Calls { get; } = [];

    public Task ScheduleOrRescheduleAsync(
        Guid definitionId,
        TimeOnly deadlineTime,
        CancellationToken ct = default)
    {
        Calls.Add((definitionId, deadlineTime));
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// UpsertHealthMonitorDefinitionHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class UpsertHealthMonitorDefinitionHandlerTests
{
    private readonly HealthDef_DefinitionRepo _repo = new();
    private readonly SpyHealthJobScheduler _scheduler = new();
    private readonly UpsertHealthMonitorDefinitionHandler _sut;

    public UpsertHealthMonitorDefinitionHandlerTests()
    {
        _sut = new UpsertHealthMonitorDefinitionHandler(
            _repo,
            _scheduler,
            NullLogger<UpsertHealthMonitorDefinitionHandler>.Instance);
    }

    private static UpsertHealthMonitorDefinitionCommand MakeCommand(Guid? id = null) =>
        new(
            DefinitionId: id,
            SystemId: "SYS-01",
            Name: "Test Rule",
            DeadlineTime: new TimeOnly(18, 0),
            ScheduleType: ScheduleType.Daily,
            CronExpression: null,
            ScheduleDayOfWeek: null,
            WatchedComponents: [new WatchedComponent("COMP-01", ComponentType.Service)],
            EmailRecipients: ["test@example.com"],
            TeamsWebhookUrl: null,
            NotificationsEnabled: true);

    [TestMethod]
    public async Task HandleAsync_NewDefinition_SchedulesJob()
    {
        var command = MakeCommand();

        await _sut.HandleAsync(command);

        Assert.HasCount(1, _scheduler.Calls);
        Assert.AreEqual(command.DeadlineTime, _scheduler.Calls[0].DeadlineTime);
    }

    [TestMethod]
    public async Task HandleAsync_NewDefinition_PersistsToRepository()
    {
        var command = MakeCommand();

        var id = await _sut.HandleAsync(command);

        Assert.IsNotNull(_repo.GetStored(id));
    }

    [TestMethod]
    public async Task HandleAsync_ExistingDefinition_ReschedulesJob()
    {
        // Create first
        var originalCommand = MakeCommand();
        var id = await _sut.HandleAsync(originalCommand);
        _scheduler.Calls.Clear();

        // Update with new deadline time
        var updateCommand = MakeCommand(id) with { DeadlineTime = new TimeOnly(20, 0) };
        await _sut.HandleAsync(updateCommand);

        Assert.HasCount(1, _scheduler.Calls);
        Assert.AreEqual(new TimeOnly(20, 0), _scheduler.Calls[0].DeadlineTime);
        Assert.AreEqual(id, _scheduler.Calls[0].DefinitionId);
    }

    [TestMethod]
    public async Task HandleAsync_ExistingDefinition_UpdatesName()
    {
        var id = await _sut.HandleAsync(MakeCommand());

        await _sut.HandleAsync(MakeCommand(id) with { Name = "Renamed Rule" });

        Assert.AreEqual("Renamed Rule", _repo.GetStored(id)!.Name);
    }

    [TestMethod]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await _sut.HandleAsync(null!));
    }
}
