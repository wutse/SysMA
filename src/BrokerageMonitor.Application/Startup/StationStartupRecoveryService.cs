using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Startup;

/// <summary>
/// Executes station-restart recovery on application startup (US-030, FR-033).
///
/// Recovery steps:
/// 1. Load all persisted <see cref="Domain.Aggregates.ComponentState"/> records
///    from SQLite into the in-memory <see cref="IComponentStateCache"/>.
/// 2. Register all active components with the heartbeat timer registry.
/// 3. For every <c>ScheduledJob</c> whose last known state is <c>Running</c>,
///    transition to <c>Warning</c> (execution result undetermined — FR-033)
///    and raise <see cref="ComponentStatusChanged"/> to trigger alert evaluation.
/// 4. Arm heartbeat timers for <c>Service</c> components that are not
///    in <c>Stopped</c> or <c>Maintenance</c> state.
/// </summary>
public sealed class StationStartupRecoveryService : IStartupRecoveryService
{
    private readonly IComponentStateRepository _stateRepository;
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IHeartbeatTimerRegistry _timerRegistry;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<StationStartupRecoveryService> _logger;

    public StationStartupRecoveryService(
        IComponentStateRepository stateRepository,
        IMonitoredComponentRepository componentRepository,
        IComponentStateCache stateCache,
        IHeartbeatTimerRegistry timerRegistry,
        IDomainEventDispatcher eventDispatcher,
        ILogger<StationStartupRecoveryService> logger)
    {
        _stateRepository = stateRepository;
        _componentRepository = componentRepository;
        _stateCache = stateCache;
        _timerRegistry = timerRegistry;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Station startup recovery started.");

        // Step 1 — hydrate in-memory cache from persistence
        var allStates = await _stateRepository.GetAllAsync(ct);
        _stateCache.LoadAll(allStates);

        _logger.LogInformation("Loaded {Count} component states from persistence.", allStates.Count);

        // Step 2 — load active component definitions
        var allComponents = await _componentRepository.GetAllActiveAsync(ct);

        // Step 3 — register all components, then handle ScheduledJob Running → Warning
        var now = DateTimeOffset.UtcNow;
        foreach (var component in allComponents)
        {
            _timerRegistry.RegisterComponent(
                component.ComponentId,
                component.SystemId,
                component.ComponentType,
                component.HeartbeatTimeoutSeconds);

            if (component.ComponentType == ComponentType.ScheduledJob)
            {
                var state = _stateCache.GetState(component.ComponentId);
                if (state?.Status == ComponentStatus.Running)
                {
                    _logger.LogWarning(
                        "ScheduledJob {ComponentId} was in Running state at startup — " +
                        "transitioning to Warning (FR-033).",
                        component.ComponentId);

                    var previousStatus = state.Status;
                    state.UpdateStatus(ComponentStatus.Warning, now);
                    await _stateRepository.UpsertAsync(state, ct);

                    await _eventDispatcher.DispatchAsync(
                        new ComponentStatusChanged(
                            component.ComponentId,
                            component.SystemId,
                            previousStatus,
                            ComponentStatus.Warning,
                            now),
                        ct);
                }
            }
        }

        // Step 4 — arm heartbeat timers for Service components not in Stopped/Maintenance
        foreach (var component in allComponents.Where(c => c.ComponentType == ComponentType.Service))
        {
            var state = _stateCache.GetState(component.ComponentId);
            var currentStatus = state?.Status ?? ComponentStatus.Unknown;

            if (currentStatus is not (ComponentStatus.Stopped or ComponentStatus.Maintenance))
            {
                _timerRegistry.ResetTimer(component.ComponentId);
            }
        }

        _logger.LogInformation("Station startup recovery completed.");
    }
}
