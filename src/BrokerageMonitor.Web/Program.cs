using BrokerageMonitor.Application;
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

    // Register SignalR and the realtime notification service
    builder.Services.AddSignalR();
    builder.Services.AddRealtimeNotifications();

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

    // Run database initialisation (WAL pragma + schema bootstrap)
    await app.Services
        .GetRequiredService<DatabaseInitializer>()
        .InitialiseAsync();

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
