using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Application.Startup;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Application.UseCases.Alerts;
using BrokerageMonitor.Application.UseCases.Dashboard;
using BrokerageMonitor.Application.UseCases.History;
using BrokerageMonitor.Application.UseCases.Maintenance;
using BrokerageMonitor.Application.UseCases.Management;
using BrokerageMonitor.Application.UseCases.StateOverride;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Application;

/// <summary>
/// DI registration for Application-layer services, use-case handlers,
/// and null-object stubs for infrastructure contracts not yet implemented.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers Application-layer singletons, scoped services, and use-case handlers.
    /// Call after <c>AddPersistence()</c> in the host composition root.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ---- In-memory state caches (singleton) ----
        services.AddSingleton<ComponentStateCache>();
        services.AddSingleton<IComponentStateCache>(sp =>
            sp.GetRequiredService<ComponentStateCache>());

        // ---- Domain services (singleton) ----
        services.AddSingleton<IStateRollupService, StateRollupService>();
        services.AddSingleton<MonitorBroadcaster>();
        services.AddSingleton<IMonitorBroadcaster>(sp =>
            sp.GetRequiredService<MonitorBroadcaster>());

        // ---- Domain event dispatcher (scoped) ----
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // ---- Heartbeat processor (scoped — depends on scoped repositories and dispatcher) ----
        services.AddScoped<IHeartbeatProcessor, HeartbeatProcessor>();

        // ---- Aggregate health evaluation stub (no-op until US-049/050 are implemented) ----
        services.AddSingleton<IAggregateHealthEvaluationService, NullAggregateHealthEvaluationService>();

        // ---- Heartbeat timer registry stub (no-op until ZeroMQ is wired via AddZeroMq) ----
        services.AddSingleton<IHeartbeatTimerRegistry, NullHeartbeatTimerRegistry>();

        // ---- Audit logger stub (no-op until persistence layer is wired) ----
        services.AddSingleton<IAuditLogger, NullAuditLogger>();

        // ---- Email notification stub ----
        services.AddSingleton<IEmailNotificationService, NullEmailNotificationService>();

        // ---- Use-case handlers (scoped) ----
        services.AddScoped<GetDashboardQueryHandler>();
        services.AddScoped<AcknowledgeAlertHandler>();
        services.AddScoped<ToggleMaintenanceModeHandler>();
        services.AddScoped<OverrideComponentStateHandler>();
        services.AddScoped<UpsertMonitoredSystemHandler>();
        services.AddScoped<UpsertMonitoredComponentHandler>();
        services.AddScoped<AlertEvaluationService>();
        services.AddScoped<IAlertEvaluationService>(sp =>
            sp.GetRequiredService<AlertEvaluationService>());
        services.AddScoped<GetExecutionHistoryQueryHandler>();
        services.AddScoped<GetAuditLogsQueryHandler>();

        // ---- Startup utilities (scoped — depend on scoped repositories) ----
        services.AddScoped<AppSettingsImporter>();

        return services;
    }
}

// ---------------------------------------------------------------------------
// Null-object stubs for contracts without a concrete implementation yet
// ---------------------------------------------------------------------------

internal sealed class NullAuditLogger : IAuditLogger
{
    public Task LogStatusChangedAsync(
        string systemId, string componentId,
        ComponentStatus previous, ComponentStatus current,
        DateTimeOffset occurredAt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task LogOperatorActionAsync(
        string systemId, string? componentId,
        string actionType, string operatorName,
        string? reason, DateTimeOffset occurredAt,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class NullAggregateHealthEvaluationService : IAggregateHealthEvaluationService
{
    public Task UpdateComponentProgressAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class NullHeartbeatTimerRegistry : IHeartbeatTimerRegistry
{
    public void RegisterComponent(string componentId, string systemId, ComponentType componentType, int timeoutSeconds) { }
    public void ResetTimer(string componentId) { }
    public void UnregisterComponent(string componentId) { }
}

internal sealed class NullEmailNotificationService : IEmailNotificationService
{
    public Task SendAlertAsync(
        AlertEmailRequest request,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendHealthSummaryAsync(
        HealthSummaryEmailRequest request,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
