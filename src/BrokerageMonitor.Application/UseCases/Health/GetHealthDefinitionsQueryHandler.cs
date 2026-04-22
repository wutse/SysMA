using BrokerageMonitor.Application.DTOs;
using BrokerageMonitor.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.UseCases.Health;

/// <summary>
/// Query that returns all health monitor definitions for a given system
/// (or all systems if <see cref="SystemId"/> is null), enriched with
/// today's <see cref="DailyExecutionDto"/> if it exists.
/// US-046 (EP-009). FR-046, FR-047.
/// </summary>
public sealed record GetHealthDefinitionsQuery(string? SystemId = null);

/// <summary>
/// Handles <see cref="GetHealthDefinitionsQuery"/>.
/// US-046 (EP-009).
/// </summary>
public sealed class GetHealthDefinitionsQueryHandler
{
    private readonly IHealthMonitorDefinitionRepository _definitionRepo;
    private readonly IDailyExecutionRepository _executionRepo;
    private readonly IMonitoredComponentRepository _componentRepo;
    private readonly ILogger<GetHealthDefinitionsQueryHandler> _logger;

    public GetHealthDefinitionsQueryHandler(
        IHealthMonitorDefinitionRepository definitionRepo,
        IDailyExecutionRepository executionRepo,
        IMonitoredComponentRepository componentRepo,
        ILogger<GetHealthDefinitionsQueryHandler> logger)
    {
        _definitionRepo = definitionRepo;
        _executionRepo = executionRepo;
        _componentRepo = componentRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HealthMonitorDefinitionDto>> HandleAsync(
        GetHealthDefinitionsQuery query,
        CancellationToken ct = default)
    {
        var definitions = string.IsNullOrEmpty(query.SystemId)
            ? await _definitionRepo.GetAllActiveAsync(ct).ConfigureAwait(false)
            : await _definitionRepo.GetBySystemIdAsync(query.SystemId, ct).ConfigureAwait(false);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayExecutions = await _executionRepo.GetByDateAsync(today, ct).ConfigureAwait(false);
        var executionByDefinition = todayExecutions.ToDictionary(e => e.DefinitionId);

        var allComponents = await _componentRepo.GetAllActiveAsync(ct).ConfigureAwait(false);
        var componentNameById = allComponents.ToDictionary(
            c => c.ComponentId, c => c.Name, StringComparer.OrdinalIgnoreCase);

        var result = new List<HealthMonitorDefinitionDto>(definitions.Count);
        foreach (var definition in definitions)
        {
            executionByDefinition.TryGetValue(definition.DefinitionId, out var execution);

            var watchedDtos = definition.WatchedComponents
                .Select(w => new WatchedComponentDto(
                    w.ComponentId,
                    componentNameById.GetValueOrDefault(w.ComponentId, w.ComponentId),
                    w.ComponentType))
                .ToList();

            DailyExecutionDto? executionDto = null;
            if (execution is not null)
            {
                executionDto = new DailyExecutionDto(
                    execution.ExecutionId,
                    execution.DefinitionId,
                    execution.SystemId,
                    execution.ExecutionDate,
                    execution.Status,
                    execution.CompletedComponents,
                    execution.FailedComponents,
                    execution.MissedReason,
                    execution.EvaluatedAt,
                    execution.NotificationSentAt);
            }

            result.Add(new HealthMonitorDefinitionDto(
                definition.DefinitionId,
                definition.SystemId,
                definition.Name,
                definition.DeadlineTime,
                definition.Schedule.ScheduleType,
                definition.Schedule.CronExpression,
                definition.Schedule.DayOfWeek,
                watchedDtos,
                definition.EmailRecipients.Select(e => e.Value).ToList(),
                definition.TeamsWebhookUrl,
                definition.SendOnFailure,
                definition.IsActive,
                executionDto));
        }

        _logger.LogDebug(
            "GetHealthDefinitionsQuery returned {Count} definitions.", result.Count);

        return result;
    }
}
