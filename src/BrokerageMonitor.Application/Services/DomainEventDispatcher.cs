using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// In-process domain event dispatcher for the Application layer.
/// Dispatches <see cref="ComponentStatusChanged"/> and <see cref="ComponentLost"/>
/// to all downstream handlers independently — one handler's failure never
/// propagates to other subscribers (US-028).
///
/// <para>
/// Handler registration uses a type-keyed dictionary built at construction time.
/// Adding support for a new domain event does not require modifying this class —
/// register an additional entry in the constructor or introduce a full
/// <c>IDomainEventHandler&lt;TEvent&gt;</c> DI registration if the catalogue grows.
/// </para>
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IReadOnlyDictionary<Type, Func<IDomainEvent, CancellationToken, Task>> _handlers;

    public DomainEventDispatcher(
        IAlertEvaluationService alertEvaluation,
        IAggregateHealthEvaluationService healthEvaluation,
        IRealtimeNotificationService realtimeNotification,
        IAuditLogger auditLogger,
        IComponentStateCache stateCache,
        ILogger<DomainEventDispatcher> logger)
    {
        _handlers = new Dictionary<Type, Func<IDomainEvent, CancellationToken, Task>>
        {
            [typeof(ComponentStatusChanged)] = (e, ct) =>
                DispatchComponentStatusChangedAsync((ComponentStatusChanged)e, ct,
                    alertEvaluation, healthEvaluation, realtimeNotification, auditLogger, logger),

            [typeof(ComponentLost)] = (e, ct) =>
                DispatchComponentLostAsync((ComponentLost)e, ct,
                    alertEvaluation, realtimeNotification, auditLogger, stateCache, logger),

            [typeof(ComponentStateOverridden)] = (e, ct) =>
                DispatchComponentStateOverriddenAsync((ComponentStateOverridden)e, ct,
                    alertEvaluation, logger),
        };
    }

    /// <inheritdoc />
    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_handlers.TryGetValue(@event.GetType(), out var handler))
            await handler(@event, ct).ConfigureAwait(false);

        // Events with no registered handler are intentionally unhandled at this layer;
        // infrastructure services (HeartbeatTimeoutMonitor, SignalR) handle them directly.
    }

    // -------------------------------------------------------------------------
    // Private dispatch methods — each handler is isolated via SafeInvokeAsync
    // -------------------------------------------------------------------------

    private static async Task DispatchComponentStatusChangedAsync(
        ComponentStatusChanged evt, CancellationToken ct,
        IAlertEvaluationService alertEvaluation,
        IAggregateHealthEvaluationService healthEvaluation,
        IRealtimeNotificationService realtimeNotification,
        IAuditLogger auditLogger,
        ILogger logger)
    {
        await SafeInvokeAsync(
            () => alertEvaluation.EvaluateAsync(evt, ct),
            nameof(IAlertEvaluationService), logger).ConfigureAwait(false);

        await SafeInvokeAsync(
            () => healthEvaluation.UpdateComponentProgressAsync(evt, ct),
            nameof(IAggregateHealthEvaluationService), logger).ConfigureAwait(false);

        await SafeInvokeAsync(
            () => realtimeNotification.NotifyComponentStatusChangedAsync(evt, ct),
            nameof(IRealtimeNotificationService), logger).ConfigureAwait(false);

        await SafeInvokeAsync(
            () => auditLogger.LogStatusChangedAsync(
                evt.SystemId, evt.ComponentId,
                evt.PreviousStatus, evt.NewStatus,
                evt.OccurredAt, ct),
            nameof(IAuditLogger), logger).ConfigureAwait(false);
    }

    private static async Task DispatchComponentStateOverriddenAsync(
        ComponentStateOverridden evt, CancellationToken ct,
        IAlertEvaluationService alertEvaluation,
        ILogger logger)
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
            () => alertEvaluation.EvaluateAsync(syntheticChange, ct),
            nameof(IAlertEvaluationService), logger).ConfigureAwait(false);
    }

    private static async Task DispatchComponentLostAsync(
        ComponentLost evt, CancellationToken ct,
        IAlertEvaluationService alertEvaluation,
        IRealtimeNotificationService realtimeNotification,
        IAuditLogger auditLogger,
        IComponentStateCache stateCache,
        ILogger logger)
    {
        // Look up actual previous status from the in-memory cache so audit entries
        // record the correct previous state instead of always logging Unknown.
        var previousStatus = stateCache.GetState(evt.ComponentId)?.Status
                             ?? ComponentStatus.Unknown;

        // ComponentLost re-routes through AlertEvaluationService via a synthetic status change
        var syntheticChange = new ComponentStatusChanged(
            evt.ComponentId,
            evt.SystemId,
            PreviousStatus: previousStatus,
            NewStatus: ComponentStatus.Lost,
            evt.OccurredAt);

        await SafeInvokeAsync(
            () => alertEvaluation.EvaluateAsync(syntheticChange, ct),
            nameof(IAlertEvaluationService), logger).ConfigureAwait(false);

        await SafeInvokeAsync(
            () => realtimeNotification.NotifyComponentStatusChangedAsync(syntheticChange, ct),
            nameof(IRealtimeNotificationService), logger).ConfigureAwait(false);

        await SafeInvokeAsync(
            () => auditLogger.LogStatusChangedAsync(
                evt.SystemId, evt.ComponentId,
                previousStatus,
                ComponentStatus.Lost,
                evt.OccurredAt, ct),
            nameof(IAuditLogger), logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes <paramref name="handler"/> and swallows any exception,
    /// logging a warning so other subscribers are not affected (US-028).
    /// </summary>
    private static async Task SafeInvokeAsync(Func<Task> handler, string handlerName, ILogger logger)
    {
        try
        {
            await handler().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Respect cancellation but still allow other handlers to run.
            logger.LogWarning(
                "Handler {HandlerName} was cancelled during event dispatch.",
                handlerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Handler {HandlerName} threw an unhandled exception during event dispatch. " +
                "Other subscribers are unaffected.",
                handlerName);
        }
    }
}
