using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Health;

/// <summary>
/// Command to create or update a <see cref="HealthMonitorDefinition"/>.
/// US-047 (EP-009). FR-041, FR-042, FR-046, FR-047.
/// </summary>
public sealed record UpsertHealthMonitorDefinitionCommand(
    Guid? DefinitionId,
    string SystemId,
    string Name,
    TimeOnly DeadlineTime,
    ScheduleType ScheduleType,
    string? CronExpression,
    DayOfWeek? ScheduleDayOfWeek,
    IReadOnlyList<WatchedComponent> WatchedComponents,
    IReadOnlyList<string> EmailRecipients,
    string? TeamsWebhookUrl,
    bool SendOnFailure);

/// <summary>
/// Handles <see cref="UpsertHealthMonitorDefinitionCommand"/>.
/// Creates a new <see cref="HealthMonitorDefinition"/> or updates an existing one.
/// US-047 (EP-009).
/// </summary>
public sealed class UpsertHealthMonitorDefinitionHandler
{
    private readonly IHealthMonitorDefinitionRepository _definitionRepo;
    private readonly IHealthJobScheduler _jobScheduler;
    private readonly ILogger<UpsertHealthMonitorDefinitionHandler> _logger;

    public UpsertHealthMonitorDefinitionHandler(
        IHealthMonitorDefinitionRepository definitionRepo,
        IHealthJobScheduler jobScheduler,
        ILogger<UpsertHealthMonitorDefinitionHandler> logger)
    {
        _definitionRepo = definitionRepo;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(
        UpsertHealthMonitorDefinitionCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var schedule = new HealthRuleSchedule(
            command.ScheduleType,
            command.CronExpression,
            command.ScheduleDayOfWeek);

        var emailRecipients = command.EmailRecipients
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => new EmailAddress(e))
            .ToList();

        HealthMonitorDefinition definition;

        if (command.DefinitionId.HasValue)
        {
            var existing = await _definitionRepo
                .GetByIdAsync(command.DefinitionId.Value, ct)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Update existing definition via domain methods
                existing.Rename(command.Name);
                existing.UpdateDeadlineTime(command.DeadlineTime);
                existing.UpdateSchedule(schedule);
                existing.SetWatchedComponents(command.WatchedComponents);
                existing.SetEmailRecipients(emailRecipients);
                existing.SetTeamsWebhookUrl(command.TeamsWebhookUrl);
                existing.SetSendOnFailure(command.SendOnFailure);

                await _definitionRepo.UpsertAsync(existing, ct).ConfigureAwait(false);
                await _jobScheduler.ScheduleOrRescheduleAsync(existing.DefinitionId, existing.DeadlineTime, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Updated HealthMonitorDefinition {DefinitionId} for system {SystemId}.",
                    existing.DefinitionId, command.SystemId);

                return existing.DefinitionId;
            }
        }

        // Create new definition
        var definitionId = command.DefinitionId ?? Guid.NewGuid();
        definition = new HealthMonitorDefinition(
            definitionId,
            command.SystemId,
            command.Name,
            command.DeadlineTime,
            schedule,
            command.WatchedComponents,
            emailRecipients,
            command.TeamsWebhookUrl,
            command.SendOnFailure);

        await _definitionRepo.UpsertAsync(definition, ct).ConfigureAwait(false);
        await _jobScheduler.ScheduleOrRescheduleAsync(definition.DefinitionId, definition.DeadlineTime, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created HealthMonitorDefinition {DefinitionId} for system {SystemId}.",
            definitionId, command.SystemId);

        return definitionId;
    }
}
