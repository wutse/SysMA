using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Processes incoming ZeroMQ heartbeat / status-update messages (FR-002, FR-003).
/// Applies sub-indicator worst-case rollup, drives state-machine transitions,
/// persists state, resets the per-component heartbeat timer, and raises domain events.
/// US-026.
/// </summary>
public sealed class HeartbeatProcessor : IHeartbeatProcessor
{
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IComponentStateRepository _stateRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IHeartbeatTimerRegistry _timerRegistry;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<HeartbeatProcessor> _logger;

    public HeartbeatProcessor(
        IMonitoredComponentRepository componentRepository,
        IComponentStateRepository stateRepository,
        IComponentStateCache stateCache,
        IHeartbeatTimerRegistry timerRegistry,
        IDomainEventDispatcher eventDispatcher,
        ILogger<HeartbeatProcessor> logger)
    {
        _componentRepository = componentRepository;
        _stateRepository = stateRepository;
        _stateCache = stateCache;
        _timerRegistry = timerRegistry;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ProcessAsync(HeartbeatMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var component = await _componentRepository.GetByIdAsync(message.ComponentId, ct);
        if (component is null || !component.IsActive)
        {
            _logger.LogWarning(
                "ERR_HB_UNKNOWN: Received heartbeat for unknown or inactive component {ComponentId}",
                message.ComponentId);
            return;
        }

        if (!Enum.TryParse<ComponentStatus>(message.Status, ignoreCase: true, out var reportedStatus))
        {
            _logger.LogWarning(
                "ERR_HB_INVALID_STATUS: Unknown status value '{Status}' for component {ComponentId}",
                message.Status, message.ComponentId);
            return;
        }

        var subIndicators = MapSubIndicators(message.SubIndicators);
        var rolledUpStatus = ComputeRolledUpStatus(reportedStatus, subIndicators);

        var currentState = _stateCache.GetState(message.ComponentId)
            ?? new ComponentState(message.ComponentId, ComponentStatus.Unknown);

        var previousStatus = currentState.Status;

        currentState.RecordHeartbeat(message.Timestamp);
        currentState.SetSubIndicators(subIndicators);

        bool statusChanged = previousStatus != rolledUpStatus;
        if (statusChanged)
        {
            currentState.UpdateStatus(rolledUpStatus, message.Timestamp);
        }

        _stateCache.SetState(currentState);
        await _stateRepository.UpsertAsync(currentState, ct);

        // Register component and reset timer; Stopped state is exempt from timeout (BI-011)
        _timerRegistry.RegisterComponent(
            message.ComponentId,
            message.SystemId,
            component.ComponentType,
            component.HeartbeatTimeoutSeconds);

        if (rolledUpStatus != ComponentStatus.Stopped)
        {
            _timerRegistry.ResetTimer(message.ComponentId);
        }

        await _eventDispatcher.DispatchAsync(
            new ComponentHeartbeatReceived(
                message.ComponentId,
                message.SystemId,
                reportedStatus,
                message.Timestamp),
            ct);

        if (statusChanged)
        {
            await _eventDispatcher.DispatchAsync(
                new ComponentStatusChanged(
                    message.ComponentId,
                    message.SystemId,
                    previousStatus,
                    rolledUpStatus,
                    message.Timestamp),
                ct);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<SubIndicator> MapSubIndicators(
        IReadOnlyList<SubIndicatorPayload>? payloads)
    {
        if (payloads is null || payloads.Count == 0)
            return [];

        var result = new List<SubIndicator>(payloads.Count);
        foreach (var p in payloads)
        {
            if (!Enum.TryParse<SubIndicatorStatus>(p.Status, ignoreCase: true, out var status))
                status = SubIndicatorStatus.Normal;

            MetricValue? metric = p.Metric is not null
                ? new MetricValue(p.Metric.Label, p.Metric.Value)
                : null;

            result.Add(new SubIndicator(p.Name, status, metric));
        }
        return result.AsReadOnly();
    }

    /// <summary>
    /// Applies worst-case rollup: if any sub-indicator reports Error the component status
    /// is elevated to at least <see cref="ComponentStatus.Error"/> (FR-039).
    /// </summary>
    internal static ComponentStatus ComputeRolledUpStatus(
        ComponentStatus reported,
        IReadOnlyList<SubIndicator> subIndicators)
    {
        if (subIndicators.Count == 0)
            return reported;

        bool anyError = false;
        foreach (var si in subIndicators)
        {
            if (si.Status == SubIndicatorStatus.Error)
            {
                anyError = true;
                break;
            }
        }

        return anyError ? TakeWorstStatus(reported, ComponentStatus.Error) : reported;
    }

    /// <summary>Returns the status with higher severity using the canonical ordering.</summary>
    internal static ComponentStatus TakeWorstStatus(ComponentStatus a, ComponentStatus b) =>
        GetSeverity(a) >= GetSeverity(b) ? a : b;

    /// <summary>
    /// Canonical severity order (US-027):
    /// Lost(7) > Error(6) > Warning(5) > Unknown(4) > Stopped(3) > Idle(2) > Running(1) > Normal(0).
    /// Failed maps to Error severity; Completed maps to Normal severity.
    /// Maintenance is a special mode and is excluded from severity ranking (-1).
    /// </summary>
    internal static int GetSeverity(ComponentStatus status) => status switch
    {
        ComponentStatus.Lost => 7,
        ComponentStatus.Error => 6,
        ComponentStatus.Failed => 6,
        ComponentStatus.Warning => 5,
        ComponentStatus.Unknown => 4,
        ComponentStatus.Stopped => 3,
        ComponentStatus.Idle => 2,
        ComponentStatus.Running => 1,
        ComponentStatus.Normal => 0,
        ComponentStatus.Completed => 0,
        ComponentStatus.Maintenance => -1,
        _ => 0
    };
}
