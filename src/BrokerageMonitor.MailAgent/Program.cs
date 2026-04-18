using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Hosting;

// Initialise NLog early so startup errors are captured
var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            // Additional services registered in subsequent stories
        })
        .ConfigureLogging(logging => logging.ClearProviders())
        .UseNLog()
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "MailAgent terminated unexpectedly.");
    throw;
}
finally
{
    LogManager.Shutdown();
}
