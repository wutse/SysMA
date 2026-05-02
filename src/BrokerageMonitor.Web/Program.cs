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

    // Register ZeroMQ subscriber, heartbeat parser and timeout monitor (US-001)
    builder.Services.AddZeroMq(builder.Configuration);

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

    // Bind Scheduler cron settings from appsettings.json
    builder.Services.Configure<SchedulerOptions>(
        builder.Configuration.GetSection(SchedulerOptions.SectionName));

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

    // Run all startup orchestration steps (DB init, seeding, job scheduling, recovery)
    await new WebApplicationStartup(app).InitialiseAsync();

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
