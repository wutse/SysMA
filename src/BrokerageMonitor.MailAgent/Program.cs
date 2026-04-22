using BrokerageMonitor.MailAgent.Options;
using BrokerageMonitor.MailAgent.Outlook;
using BrokerageMonitor.MailAgent.Workers;
using BrokerageMonitor.MailAgent.ZeroMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Hosting;

// Initialise NLog early so startup errors are captured.
var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            // Bind MailAgentOptions from the "MailAgent" section of appsettings.json.
            services.Configure<MailAgentOptions>(
                context.Configuration.GetSection(MailAgentOptions.SectionName));

            // Outlook mail reader — singleton owning the dedicated STA thread.
            services.AddSingleton<IOutlookMailReader, OutlookMailReader>();

            // ZeroMQ publisher — singleton owning the PUB socket lifecycle.
            services.AddSingleton<IZeroMQMailPublisher, ZeroMQMailPublisher>();

            // Polling hosted service.
            services.AddHostedService<MailRelayWorker>();
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
