using BrokerageMonitor.Application;
using BrokerageMonitor.Application.Startup;
using BrokerageMonitor.Infrastructure;
using BrokerageMonitor.Infrastructure.Notifications;
using BrokerageMonitor.Infrastructure.Persistence;
using BrokerageMonitor.Infrastructure.Scheduling;
using BrokerageMonitor.Web.Components;
using BrokerageMonitor.Web.Services;
using NLog;
using NLog.Web;
using Quartz;

// Initialise NLog early so startup errors are captured
var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace default logging with NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Register Quartz.NET in-memory scheduler
    builder.Services.AddQuartzScheduler();

    // Register SQLite persistence (DbConnectionFactory + DatabaseInitializer)
    builder.Services.AddPersistence();

    // Register Application-layer services (handlers, state cache, broadcaster)
    builder.Services.AddApplicationServices();

    // Register startup recovery service (US-030, US-052)
    builder.Services.AddScoped<IStartupRecoveryService, StationStartupRecoveryService>();

    // Bind Systems[] from appsettings.json for AppSettingsImporter (FR-031 / US-045)
    var systemConfigs = builder.Configuration.GetSection("Systems").Get<List<SystemConfig>>() ?? [];
    foreach (var sc in systemConfigs)
        builder.Services.AddSingleton(sc);

    // Register SignalR and the realtime notification service
    builder.Services.AddSignalR();
    builder.Services.AddRealtimeNotifications();

    // Register SMTP and Teams notification services (US-053, US-054)
    builder.Services.AddNotificationServices(builder.Configuration);

    // Register circuit-scoped operator session (one instance per Blazor Server circuit)
    builder.Services.AddScoped<OperatorSessionService>();

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Map SignalR hub
    app.MapHub<MonitorHub>("/hubs/monitor");

    // Schedule SmokeTestJob (startup verification — runs once at 01:00 each day)
    var scheduler = await app.Services
        .GetRequiredService<ISchedulerFactory>()
        .GetScheduler();
    await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
        scheduler,
        cronExpression: "0 0 1 * * ?");

    // Schedule DailyExecutionCreatorJob — runs daily at 05:30 (US-048)
    await QuartzJobScheduler.ScheduleCronJobAsync<DailyExecutionCreatorJob>(
        scheduler,
        cronExpression: "0 30 5 * * ?");

    // Schedule AggregateHealthEvaluationJob — dynamically per definition at startup (US-051)
    // Jobs per definition are scheduled after DB init, inside the startup scope below.

    // Run database initialisation (WAL pragma + schema bootstrap)
    await app.Services
        .GetRequiredService<DatabaseInitializer>()
        .InitialiseAsync();

    // Seed initial systems from appsettings.json if DB is empty (US-045)
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<AppSettingsImporter>()
            .ImportIfEmptyAsync();
    }

    // Schedule AggregateHealthEvaluationJob per definition (US-051)
    using (var scope = app.Services.CreateScope())
    {
        var definitionRepo = scope.ServiceProvider
            .GetRequiredService<BrokerageMonitor.Domain.Repositories.IHealthMonitorDefinitionRepository>();
        var definitions = await definitionRepo.GetAllActiveAsync();
        foreach (var def in definitions)
        {
            var jobData = new JobDataMap
            {
                [AggregateHealthEvaluationJob.DefinitionIdKey] = def.DefinitionId.ToString()
            };
            var cron = $"0 {def.DeadlineTime.Minute} {def.DeadlineTime.Hour} * * ?";
            await QuartzJobScheduler.ScheduleCronJobAsync<AggregateHealthEvaluationJob>(
                scheduler, cron, $"AggregateHealthEval-{def.DefinitionId}", jobData);
        }
    }

    // Station startup recovery — restore component states and recover DailyExecutions (US-030, US-052)
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<IStartupRecoveryService>()
            .RecoverAsync();
    }

    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Application terminated unexpectedly.");
    throw;
}
finally
{
    LogManager.Shutdown();
}
