using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Infrastructure.Monitoring;
using BrokerageMonitor.Infrastructure.Notifications;
using BrokerageMonitor.Infrastructure.Persistence;
using BrokerageMonitor.Infrastructure.Persistence.Repositories;
using BrokerageMonitor.Infrastructure.ZeroMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BrokerageMonitor.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with the DI container.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite connection factory, database initialiser, and all repository implementations.
    /// The connection string is expected at "ConnectionStrings:BrokerageMonitor" in configuration.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();

        services.AddScoped<IMonitoredSystemRepository, MonitoredSystemRepository>();
        services.AddScoped<IMonitoredComponentRepository, MonitoredComponentRepository>();
        services.AddScoped<IComponentStateRepository, ComponentStateRepository>();
        services.AddScoped<IAlertRecordRepository, AlertRecordRepository>();
        services.AddScoped<IHealthMonitorDefinitionRepository, HealthMonitorDefinitionRepository>();
        services.AddScoped<IDailyExecutionRepository, DailyExecutionRepository>();
        services.AddScoped<IExecutionHistoryRepository, ExecutionHistoryRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<INotificationInboxRepository, NotificationInboxRepository>();

        return services;
    }

    /// <summary>
    /// Registers ZeroMQ subscriber service, message parsers, and binds <see cref="ZeroMqOptions"/>
    /// from the <c>ZeroMQ</c> configuration section.
    /// Call after <see cref="AddPersistence"/> in the host's composition root.
    /// </summary>
    public static IServiceCollection AddZeroMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ZeroMqOptions>(opts =>
            configuration.GetSection(ZeroMqOptions.SectionName).Bind(opts));

        services.AddSingleton<IHeartbeatMessageParser, HeartbeatMessageParser>();
        services.AddSingleton<IMailChannelMessageParser, MailChannelMessageParser>();

        // HeartbeatTimeoutMonitor is both a HostedService and an IHeartbeatTimerRegistry.
        // Register as singleton first so both interfaces resolve to the same instance.
        services.AddSingleton<HeartbeatTimeoutMonitor>();
        services.AddSingleton<IHeartbeatTimerRegistry>(sp =>
            sp.GetRequiredService<HeartbeatTimeoutMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatTimeoutMonitor>());

        services.AddHostedService<ZeroMQSubscriberService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="SignalRNotificationService"/> as the singleton
    /// <see cref="IRealtimeNotificationService"/> implementation.
    /// Requires that <c>services.AddSignalR()</c> has been called by the host.
    /// </summary>
    public static IServiceCollection AddRealtimeNotifications(this IServiceCollection services)
    {
        services.AddSingleton<IRealtimeNotificationService, SignalRNotificationService>();
        return services;
    }
}
