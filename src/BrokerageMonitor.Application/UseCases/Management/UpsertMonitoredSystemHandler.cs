using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Handles <see cref="UpsertMonitoredSystemCommand"/>.
/// Creates a new <see cref="MonitoredSystem"/> or overwrites an existing one (FR-031).
/// </summary>
public sealed class UpsertMonitoredSystemHandler
{
    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly ILogger<UpsertMonitoredSystemHandler> _logger;

    public UpsertMonitoredSystemHandler(
        IMonitoredSystemRepository systemRepository,
        ILogger<UpsertMonitoredSystemHandler> logger)
    {
        _systemRepository = systemRepository;
        _logger = logger;
    }

    /// <summary>
    /// Executes the upsert operation.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when required fields are empty or EmailAddress format is invalid.</exception>
    public async Task HandleAsync(UpsertMonitoredSystemCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var marketSession = new MarketSessionWindow(command.MarketSessionStart, command.MarketSessionEnd);
        var recipients = command.AlertRecipients
            .Select(e => new EmailAddress(e))
            .ToList();

        var existing = await _systemRepository.GetByIdAsync(command.SystemId, ct);

        if (existing is null)
        {
            var system = new MonitoredSystem(
                command.SystemId,
                command.Name,
                marketSession,
                recipients,
                command.IsActive);

            await _systemRepository.UpsertAsync(system, ct);
            _logger.LogInformation("Created MonitoredSystem {SystemId}.", command.SystemId);
        }
        else
        {
            existing.Rename(command.Name);
            existing.UpdateMarketSession(marketSession);
            existing.SetAlertRecipients(recipients);

            await _systemRepository.UpsertAsync(existing, ct);
            _logger.LogInformation("Updated MonitoredSystem {SystemId}.", command.SystemId);
        }
    }
}
