using System.Collections.Concurrent;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.Monitoring;

/// <summary>
/// Hosted service that manages a per-component <see cref="System.Threading.Timer"/> for heartbeat
/// timeout detection (FR-003, FR-028, BI-011, US-024).
///
/// Rules:
/// <list type="bullet">
///   <item>
///     <term>Service components</term>
///     <description>
///       Raise <see cref="ComponentLost"/> when a heartbeat timeout fires in any state
///       except <see cref="ComponentStatus.Stopped"/>.
///     </description>
///   </item>
///   <item>
///     <term>ScheduledJob components</term>
///     <description>
///       Raise <see cref="ComponentLost"/> only when the current state is
///       <see cref="ComponentStatus.Running"/>.
///     </description>
///   </item>
/// </list>
///
/// Also implements <see cref="IHeartbeatTimerRegistry"/> so that
/// <c>IHeartbeatProcessor</c> can reset timers when a heartbeat arrives (US-026).
/// </summary>
public sealed class HeartbeatTimeoutMonitor : BackgroundService, IHeartbeatTimerRegistry
{
    private readonly ConcurrentDictionary<string, TimerEntry> _timers = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatTimeoutMonitor> _logger;

    public HeartbeatTimeoutMonitor(
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatTimeoutMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ---------------------------------------------------------------
    // IHeartbeatTimerRegistry
    // ---------------------------------------------------------------

    /// <inheritdoc/>
    public void RegisterComponent(
        string componentId,
        string systemId,
        ComponentType componentType,
        int timeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);

        var dueTime = TimeSpan.FromSeconds(timeoutSeconds);

        var entry = new TimerEntry(componentId, systemId, componentType, dueTime);
        entry.Timer = new Timer(
            callback: OnTimerFired,
            state: entry,
            dueTime: dueTime,
            period: Timeout.InfiniteTimeSpan);

        if (_timers.TryRemove(componentId, out var existing))
        {
            existing.Timer?.Dispose();
        }

        _timers[componentId] = entry;

        _logger.LogDebug(
            "Registered heartbeat timer for component {ComponentId} (timeout {TimeoutSeconds}s).",
            componentId,
            timeoutSeconds);
    }

    /// <inheritdoc/>
    public void ResetTimer(string componentId)
    {
        if (_timers.TryGetValue(componentId, out var entry))
        {
            entry.Timer?.Change(entry.DueTime, Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc/>
    public void UnregisterComponent(string componentId)
    {
        if (_timers.TryRemove(componentId, out var entry))
        {
            entry.Timer?.Dispose();
            _logger.LogDebug("Unregistered heartbeat timer for component {ComponentId}.", componentId);
        }
    }

    // ---------------------------------------------------------------
    // BackgroundService
    // ---------------------------------------------------------------

    /// <summary>
    /// Loads all active components from the repository and registers their heartbeat timers.
    /// This runs once during host start-up so subsequent EP-005 processors can reset timers.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadComponentsAsync(stoppingToken).ConfigureAwait(false);

        // Keep the service alive until the host requests a stop.
        await Task.Delay(Timeout.Infinite, stoppingToken)
                  .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Dispose all timers on shutdown.
        foreach (var entry in _timers.Values)
        {
            entry.Timer?.Dispose();
        }
        _timers.Clear();
    }

    private async Task LoadComponentsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMonitoredComponentRepository>();

        IReadOnlyList<MonitoredComponent> components;
        try
        {
            components = await repo.GetAllActiveAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HeartbeatTimeoutMonitor failed to load active components.");
            return;
        }

        foreach (var component in components)
        {
            RegisterComponent(
                component.ComponentId,
                component.SystemId,
                component.ComponentType,
                component.HeartbeatTimeoutSeconds);
        }

        _logger.LogInformation(
            "HeartbeatTimeoutMonitor registered timers for {Count} active component(s).",
            components.Count);
    }

    // ---------------------------------------------------------------
    // Timer callback
    // ---------------------------------------------------------------

    private void OnTimerFired(object? state)
    {
        if (state is not TimerEntry entry)
        {
            return;
        }

        // Fire-and-forget: raise ComponentLost asynchronously without blocking the timer thread.
        _ = RaiseComponentLostAsync(entry, CancellationToken.None);
    }

    private async Task RaiseComponentLostAsync(TimerEntry entry, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IComponentStateRepository>();

        ComponentState? componentState;
        try
        {
            componentState = await stateRepo
                .GetByComponentIdAsync(entry.ComponentId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "HeartbeatTimeoutMonitor failed to read state for component {ComponentId}.",
                entry.ComponentId);
            return;
        }

        if (!ShouldRaiseComponentLost(entry.ComponentType, componentState?.Status))
        {
            return;
        }

        var @event = new ComponentLost(entry.ComponentId, entry.SystemId, DateTimeOffset.UtcNow);

        _logger.LogWarning(
            "Heartbeat timeout for component {ComponentId} (system {SystemId}, type {ComponentType}). Raising ComponentLost.",
            entry.ComponentId,
            entry.SystemId,
            entry.ComponentType);

        var dispatcher = scope.ServiceProvider.GetService<IDomainEventDispatcher>();
        if (dispatcher is not null)
        {
            try
            {
                await dispatcher.DispatchAsync(@event, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "HeartbeatTimeoutMonitor failed to dispatch ComponentLost for component {ComponentId}.",
                    entry.ComponentId);
            }
        }
    }

    /// <summary>
    /// Encapsulates the rule for whether a timer expiry should raise <see cref="ComponentLost"/>.
    /// BI-011: Service — any state except Stopped; ScheduledJob — Running state only.
    /// </summary>
    internal static bool ShouldRaiseComponentLost(
        ComponentType componentType,
        ComponentStatus? currentStatus)
    {
        if (currentStatus is null)
        {
            // Unknown state — apply Service default (raise Lost for safety).
            return componentType == ComponentType.Service;
        }

        return componentType switch
        {
            ComponentType.Service => currentStatus != ComponentStatus.Stopped,
            ComponentType.ScheduledJob => currentStatus == ComponentStatus.Running,
            _ => false,
        };
    }

    // ---------------------------------------------------------------
    // Inner record
    // ---------------------------------------------------------------

    private sealed class TimerEntry(
        string componentId,
        string systemId,
        ComponentType componentType,
        TimeSpan dueTime)
    {
        public string ComponentId { get; } = componentId;
        public string SystemId { get; } = systemId;
        public ComponentType ComponentType { get; } = componentType;
        public TimeSpan DueTime { get; } = dueTime;
        public Timer? Timer { get; set; }
    }
}
