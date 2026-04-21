using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Handles <see cref="UpsertMonitoredComponentCommand"/>.
/// Creates a new <see cref="MonitoredComponent"/> or updates an existing one (FR-031).
/// </summary>
public sealed class UpsertMonitoredComponentHandler
{
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly ILogger<UpsertMonitoredComponentHandler> _logger;

    public UpsertMonitoredComponentHandler(
        IMonitoredComponentRepository componentRepository,
        ILogger<UpsertMonitoredComponentHandler> logger)
    {
        _componentRepository = componentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Executes the upsert operation.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    public async Task HandleAsync(UpsertMonitoredComponentCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _componentRepository.GetByIdAsync(command.ComponentId, ct);

        if (existing is null)
        {
            var component = new MonitoredComponent(
                command.ComponentId,
                command.SystemId,
                command.Name,
                command.ComponentType,
                command.ZeroMQTopic,
                command.HeartbeatTimeoutSeconds,
                command.CronExpression,
                command.MailParsingRule,
                command.IsActive);

            await _componentRepository.UpsertAsync(component, ct);
            _logger.LogInformation("Created MonitoredComponent {ComponentId} in system {SystemId}.",
                command.ComponentId, command.SystemId);
        }
        else
        {
            existing.Rename(command.Name);
            existing.UpdateHeartbeatTimeout(command.HeartbeatTimeoutSeconds);
            existing.UpdateCronExpression(command.CronExpression);
            existing.SetMailParsingRule(command.MailParsingRule);

            if (command.IsActive) existing.Activate();
            else existing.Deactivate();

            await _componentRepository.UpsertAsync(existing, ct);
            _logger.LogInformation("Updated MonitoredComponent {ComponentId}.", command.ComponentId);
        }
    }
}
