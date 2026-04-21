using BrokerageMonitor.Application.UseCases.Management;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Startup;

/// <summary>
/// Reads the <c>Systems</c> array from <c>appsettings.json</c> and upserts all
/// entries into the database <b>only when the system table is empty</b> (first-run seeding).
///
/// US-045 / FR-031.
/// </summary>
public sealed class AppSettingsImporter
{
    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly UpsertMonitoredSystemHandler _upsertSystem;
    private readonly UpsertMonitoredComponentHandler _upsertComponent;
    private readonly IReadOnlyList<SystemConfig> _configs;
    private readonly ILogger<AppSettingsImporter> _logger;

    public AppSettingsImporter(
        IMonitoredSystemRepository systemRepository,
        UpsertMonitoredSystemHandler upsertSystem,
        UpsertMonitoredComponentHandler upsertComponent,
        IEnumerable<SystemConfig> configs,
        ILogger<AppSettingsImporter> logger)
    {
        _systemRepository = systemRepository;
        _upsertSystem = upsertSystem;
        _upsertComponent = upsertComponent;
        _configs = [.. configs];
        _logger = logger;
    }

    /// <summary>
    /// Imports all systems from configuration if the repository is currently empty.
    /// Safe to call on every startup — skips import when data already exists.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ImportIfEmptyAsync(CancellationToken ct = default)
    {
        if (_configs.Count == 0)
        {
            _logger.LogInformation("AppSettingsImporter: no Systems configured in appsettings.json, skipping import.");
            return;
        }

        var existing = await _systemRepository.GetAllActiveAsync(ct);
        if (existing.Count > 0)
        {
            _logger.LogInformation(
                "AppSettingsImporter: {Count} system(s) already in DB, skipping import.", existing.Count);
            return;
        }

        _logger.LogInformation(
            "AppSettingsImporter: DB is empty — importing {Count} system(s) from appsettings.json.",
            _configs.Count);

        foreach (var cfg in _configs)
        {
            try
            {
                await ImportSystemAsync(cfg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AppSettingsImporter: failed to import system {SystemId}.", cfg.SystemId);
            }
        }
    }

    private async Task ImportSystemAsync(SystemConfig cfg, CancellationToken ct)
    {
        var startTime = TimeOnly.Parse(cfg.MarketSessionStart);
        var endTime = TimeOnly.Parse(cfg.MarketSessionEnd);

        var sysCmd = new UpsertMonitoredSystemCommand(
            cfg.SystemId,
            cfg.Name,
            startTime,
            endTime,
            cfg.AlertRecipients.AsReadOnly());

        await _upsertSystem.HandleAsync(sysCmd, ct);

        _logger.LogInformation(
            "AppSettingsImporter: imported system {SystemId} with {ComponentCount} component(s).",
            cfg.SystemId, cfg.Components.Count);

        foreach (var comp in cfg.Components)
        {
            var compType = Enum.Parse<ComponentType>(comp.ComponentType, ignoreCase: true);
            var compCmd = new UpsertMonitoredComponentCommand(
                comp.ComponentId,
                cfg.SystemId,
                comp.Name,
                compType,
                comp.ZeroMQTopic,
                comp.HeartbeatTimeoutSeconds,
                comp.CronExpression,
                null,
                comp.IsActive);

            await _upsertComponent.HandleAsync(compCmd, ct);
        }
    }
}
