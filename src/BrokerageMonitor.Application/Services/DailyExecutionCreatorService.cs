using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Application service that creates daily execution instances and resets component states.
/// US-048 / US-052 (EP-009). FR-042, FR-044, BI-012, BI-015.
/// </summary>
public sealed class DailyExecutionCreatorService : IDailyExecutionCreatorService
{
    private readonly IHealthMonitorDefinitionRepository _definitionRepo;
    private readonly IDailyExecutionRepository _executionRepo;
    private readonly IComponentStateRepository _componentStateRepo;
    private readonly IComponentStateCache _stateCache;
    private readonly IRealtimeNotificationService _realtime;
    private readonly ILogger<DailyExecutionCreatorService> _logger;
    private readonly TimeProvider _timeProvider;

    public DailyExecutionCreatorService(
        IHealthMonitorDefinitionRepository definitionRepo,
        IDailyExecutionRepository executionRepo,
        IComponentStateRepository componentStateRepo,
        IComponentStateCache stateCache,
        IRealtimeNotificationService realtime,
        ILogger<DailyExecutionCreatorService> logger,
        TimeProvider timeProvider)
    {
        _definitionRepo = definitionRepo;
        _executionRepo = executionRepo;
        _componentStateRepo = componentStateRepo;
        _stateCache = stateCache;
        _realtime = realtime;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task CreateForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var definitions = await _definitionRepo.GetAllActiveAsync(ct).ConfigureAwait(false);

        foreach (var definition in definitions)
        {
            // BI-015: skip definitions whose schedule does not match the target date
            if (!definition.Schedule.IsMatch(date))
            {
                _logger.LogDebug(
                    "Definition {DefinitionId} schedule does not match {Date}. Skipping.",
                    definition.DefinitionId, date);
                continue;
            }

            await CreateExecutionAsync(definition, date, DailyExecutionStatus.InProgress, null, ct)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task RecoverTodayAsync(CancellationToken ct = default)
    {
        var localNow = _timeProvider.GetLocalNow();
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var definitions = await _definitionRepo.GetAllActiveAsync(ct).ConfigureAwait(false);
        var now = TimeOnly.FromDateTime(localNow.DateTime);

        foreach (var definition in definitions)
        {
            // BI-015: skip definitions whose schedule does not match today
            if (!definition.Schedule.IsMatch(today))
                continue;

            var deadlinePassed = now > definition.DeadlineTime;
            var status = deadlinePassed ? DailyExecutionStatus.Missed : DailyExecutionStatus.InProgress;
            var missedReason = deadlinePassed ? "站台未運行" : null;

            await CreateExecutionAsync(definition, today, status, missedReason, ct)
                .ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task CreateExecutionAsync(
        HealthMonitorDefinition definition,
        DateOnly date,
        DailyExecutionStatus initialStatus,
        string? missedReason,
        CancellationToken ct)
    {
        // BI-012: skip if execution already exists for this definition+date
        var existing = await _executionRepo
            .GetByDefinitionAndDateAsync(definition.DefinitionId, date, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogDebug(
                "DailyExecution already exists for definition {DefinitionId} on {Date}. Skipping (BI-012).",
                definition.DefinitionId, date);
            return;
        }

        var execution = new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: definition.DefinitionId,
            systemId: definition.SystemId,
            executionDate: date,
            initialStatus: initialStatus,
            missedReason: missedReason,
            createdAt: _timeProvider.GetUtcNow());

        await _executionRepo.AddAsync(execution, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created DailyExecution {ExecutionId} ({Status}) for definition {DefinitionId} on {Date}.",
            execution.ExecutionId, initialStatus, definition.DefinitionId, date);

        // Reset ScheduledJob components to Idle after InProgress execution creation
        if (initialStatus == DailyExecutionStatus.InProgress)
        {
            await ResetScheduledJobComponentsAsync(definition, ct).ConfigureAwait(false);
        }

        // Push real-time notification
        await NotifyAsync(execution.ExecutionId, ct).ConfigureAwait(false);
    }

    private async Task ResetScheduledJobComponentsAsync(
        HealthMonitorDefinition definition,
        CancellationToken ct)
    {
        var scheduledJobComponents = definition.WatchedComponents
            .Where(w => w.ComponentType == ComponentType.ScheduledJob)
            .Select(w => w.ComponentId)
            .ToList();

        foreach (var componentId in scheduledJobComponents)
        {
            var state = _stateCache.GetState(componentId);
            if (state is null)
                continue;

            var previous = state.Status;
            state.UpdateStatus(ComponentStatus.Idle, _timeProvider.GetUtcNow());
            _stateCache.SetState(state);

            await _componentStateRepo.UpsertAsync(state, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Reset ScheduledJob component {ComponentId} from {Previous} to Idle.",
                componentId, previous);
        }
    }

    private async Task NotifyAsync(Guid executionId, CancellationToken ct)
    {
        try
        {
            await _realtime.NotifyDailyExecutionUpdatedAsync(executionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push OnDailyExecutionUpdated for execution {ExecutionId}.", executionId);
        }
    }
}
