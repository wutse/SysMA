using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Events;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// In-process domain event dispatcher for the Application layer.
/// Dispatches <see cref="ComponentStatusChanged"/> and <see cref="ComponentLost"/>
/// to all downstream handlers independently — one handler's failure never
/// propagates to other subscribers (US-028).
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IAlertEvaluationService _alertEvaluation;
    private readonly IAggregateHealthEvaluationService _healthEvaluation;
    private readonly IRealtimeNotificationService _realtimeNotification;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(
        IAlertEvaluationService alertEvaluation,
        IAggregateHealthEvaluationService healthEvaluation,
        IRealtimeNotificationService realtimeNotification,
        IAuditLogger auditLogger,
        ILogger<DomainEventDispatcher> logger)
    {
        _alertEvaluation = alertEvaluation;
        _healthEvaluation = healthEvaluation;
        _realtimeNotification = realtimeNotification;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        switch (@event)
        {
            case ComponentStatusChanged e:
                await DispatchComponentStatusChangedAsync(e, ct);
                break;

            case ComponentLost e:
                await DispatchComponentLostAsync(e, ct);
                break;

            case ComponentStateOverridden e:
                await DispatchComponentStateOverriddenAsync(e, ct);
                break;

            // Other events are intentionally unhandled at this layer;
            // infrastructure services (HeartbeatTimeoutMonitor, SignalR) handle them directly.
            default:
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Private dispatch methods — each handler is isolated via SafeInvokeAsync
    // -------------------------------------------------------------------------

    private async Task DispatchComponentStatusChangedAsync(
        ComponentStatusChanged evt, CancellationToken ct)
    {
        await SafeInvokeAsync(
            () => _alertEvaluation.EvaluateAsync(evt, ct),
            nameof(IAlertEvaluationService));

        await SafeInvokeAsync(
            () => _healthEvaluation.UpdateComponentProgressAsync(evt, ct),
            nameof(IAggregateHealthEvaluationService));

        await SafeInvokeAsync(
            () => _realtimeNotification.NotifyComponentStatusChangedAsync(evt, ct),
            nameof(IRealtimeNotificationService));

        await SafeInvokeAsync(
            () => _auditLogger.LogStatusChangedAsync(
                evt.SystemId, evt.ComponentId,
                evt.PreviousStatus, evt.NewStatus,
                evt.OccurredAt, ct),
            nameof(IAuditLogger));
    }

    private async Task DispatchComponentStateOverriddenAsync(
        ComponentStateOverridden evt, CancellationToken ct)
    {
        // Route through AlertEvaluationService via a synthetic ComponentStatusChanged
        // so active alerts are cleared and new ones are raised for the overridden state.
        var syntheticChange = new ComponentStatusChanged(
            evt.ComponentId,
            evt.SystemId,
            PreviousStatus: evt.PreviousStatus,
            NewStatus: evt.NewStatus,
            evt.OccurredAt);

        await SafeInvokeAsync(
            () => _alertEvaluation.EvaluateAsync(syntheticChange, ct),
            nameof(IAlertEvaluationService));
    }

    private async Task DispatchComponentLostAsync(ComponentLost evt, CancellationToken ct)
    {
        // ComponentLost re-routes through AlertEvaluationService via a synthetic status change
        var syntheticChange = new ComponentStatusChanged(
            evt.ComponentId,
            evt.SystemId,
            PreviousStatus: Domain.ValueObjects.ComponentStatus.Unknown, // actual previous unknown at this point
            NewStatus: Domain.ValueObjects.ComponentStatus.Lost,
            evt.OccurredAt);

        await SafeInvokeAsync(
            () => _alertEvaluation.EvaluateAsync(syntheticChange, ct),
            nameof(IAlertEvaluationService));

        await SafeInvokeAsync(
            () => _realtimeNotification.NotifyComponentStatusChangedAsync(syntheticChange, ct),
            nameof(IRealtimeNotificationService));

        await SafeInvokeAsync(
            () => _auditLogger.LogStatusChangedAsync(
                evt.SystemId, evt.ComponentId,
                Domain.ValueObjects.ComponentStatus.Unknown,
                Domain.ValueObjects.ComponentStatus.Lost,
                evt.OccurredAt, ct),
            nameof(IAuditLogger));
    }

    /// <summary>
    /// Executes <paramref name="handler"/> and swallows any exception,
    /// logging a warning so other subscribers are not affected (US-028).
    /// </summary>
    private async Task SafeInvokeAsync(Func<Task> handler, string handlerName)
    {
        try
        {
            await handler();
        }
        catch (OperationCanceledException)
        {
            // Respect cancellation but still allow other handlers to run.
            _logger.LogWarning(
                "Handler {HandlerName} was cancelled during event dispatch.",
                handlerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Handler {HandlerName} threw an unhandled exception during event dispatch. " +
                "Other subscribers are unaffected.",
                handlerName);
        }
    }
}
