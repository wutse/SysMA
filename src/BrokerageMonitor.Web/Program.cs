using BrokerageMonitor.Infrastructure.Scheduling;
using BrokerageMonitor.Web.Components;
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

    // Schedule SmokeTestJob (startup verification — runs once at 01:00 each day)
    var scheduler = await app.Services
        .GetRequiredService<ISchedulerFactory>()
        .GetScheduler();
    await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
        scheduler,
        cronExpression: "0 0 1 * * ?");

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
