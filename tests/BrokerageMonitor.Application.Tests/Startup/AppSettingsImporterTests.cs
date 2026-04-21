using BrokerageMonitor.Application.Startup;
using BrokerageMonitor.Application.UseCases.Management;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Startup;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class AppImport_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = new();

    public void Seed(MonitoredSystem system) => _store[system.SystemId] = system;

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _store.Values]);

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        _store[system.SystemId] = system;
        return Task.CompletedTask;
    }

    public Task SetMaintenanceModeAsync(string systemId, bool active, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AppImport_ComponentRepo : IMonitoredComponentRepository
{
    public List<MonitoredComponent> Upserted { get; } = [];

    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult<MonitoredComponent?>(null);

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([]);

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([]);

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
    {
        Upserted.Add(component);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// AppSettingsImporterTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AppSettingsImporterTests
{
    private static AppSettingsImporter BuildSut(
        AppImport_SystemRepo sysRepo,
        AppImport_ComponentRepo compRepo,
        List<SystemConfig> configs)
    {
        var upsertSys = new UpsertMonitoredSystemHandler(sysRepo, NullLogger<UpsertMonitoredSystemHandler>.Instance);
        var upsertComp = new UpsertMonitoredComponentHandler(compRepo, NullLogger<UpsertMonitoredComponentHandler>.Instance);

        return new AppSettingsImporter(sysRepo, upsertSys, upsertComp, configs, NullLogger<AppSettingsImporter>.Instance);
    }

    // ── ImportIfEmptyAsync — skip when non-empty ──────────────────────────────

    [TestMethod]
    public async Task ImportIfEmptyAsync_DbNotEmpty_SkipsImport()
    {
        // Arrange
        var sysRepo = new AppImport_SystemRepo();
        var compRepo = new AppImport_ComponentRepo();

        // Seed existing system so DB is not empty
        sysRepo.Seed(new MonitoredSystem(
            "SYS-EXIST", "Existing", new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(13, 30))));

        var configs = new List<SystemConfig>
        {
            new() { SystemId = "SYS-NEW", Name = "New", MarketSessionStart = "09:00", MarketSessionEnd = "13:30" }
        };

        var sut = BuildSut(sysRepo, compRepo, configs);

        // Act
        await sut.ImportIfEmptyAsync();

        // Assert — SYS-NEW must NOT have been created
        Assert.IsNull(await sysRepo.GetByIdAsync("SYS-NEW"));
    }

    // ── ImportIfEmptyAsync — imports when empty ───────────────────────────────

    [TestMethod]
    public async Task ImportIfEmptyAsync_DbEmpty_ImportsAllSystems()
    {
        // Arrange
        var sysRepo = new AppImport_SystemRepo();
        var compRepo = new AppImport_ComponentRepo();

        var configs = new List<SystemConfig>
        {
            new() { SystemId = "SYS-A", Name = "Alpha", MarketSessionStart = "09:00", MarketSessionEnd = "13:30" },
            new() { SystemId = "SYS-B", Name = "Beta",  MarketSessionStart = "09:30", MarketSessionEnd = "14:00" }
        };

        var sut = BuildSut(sysRepo, compRepo, configs);

        // Act
        await sut.ImportIfEmptyAsync();

        // Assert
        Assert.IsNotNull(await sysRepo.GetByIdAsync("SYS-A"));
        Assert.IsNotNull(await sysRepo.GetByIdAsync("SYS-B"));
    }

    [TestMethod]
    public async Task ImportIfEmptyAsync_SystemWithComponents_ImportsComponents()
    {
        // Arrange
        var sysRepo = new AppImport_SystemRepo();
        var compRepo = new AppImport_ComponentRepo();

        var configs = new List<SystemConfig>
        {
            new()
            {
                SystemId = "SYS-C",
                Name = "Charlie",
                MarketSessionStart = "09:00",
                MarketSessionEnd = "13:30",
                Components =
                [
                    new() { ComponentId = "COMP-C1", Name = "Service 1", ComponentType = "Service",
                            ZeroMQTopic = "topic.c1", HeartbeatTimeoutSeconds = 30 },
                    new() { ComponentId = "COMP-C2", Name = "Job 1", ComponentType = "ScheduledJob",
                            ZeroMQTopic = "topic.c2", HeartbeatTimeoutSeconds = 60,
                            CronExpression = "0 30 9 * * ?" }
                ]
            }
        };

        var sut = BuildSut(sysRepo, compRepo, configs);

        // Act
        await sut.ImportIfEmptyAsync();

        // Assert
        Assert.HasCount(2, compRepo.Upserted);
    }

    // ── ImportIfEmptyAsync — no configs ───────────────────────────────────────

    [TestMethod]
    public async Task ImportIfEmptyAsync_NoConfigs_DoesNothing()
    {
        // Arrange
        var sysRepo = new AppImport_SystemRepo();
        var compRepo = new AppImport_ComponentRepo();
        var sut = BuildSut(sysRepo, compRepo, []);

        // Act
        await sut.ImportIfEmptyAsync();

        // Assert — nothing upserted
        var allSystems = await sysRepo.GetAllActiveAsync();
        Assert.IsEmpty(allSystems);
    }
}
