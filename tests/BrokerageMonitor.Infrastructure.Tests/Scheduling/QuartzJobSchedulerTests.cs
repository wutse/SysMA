using System.Collections.Specialized;
using BrokerageMonitor.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl;

namespace BrokerageMonitor.Infrastructure.Tests.Scheduling;

[TestClass]
public sealed class QuartzJobSchedulerTests
{
  // ---------------------------------------------------------------
  // AddQuartzScheduler — DI registration tests
  // ---------------------------------------------------------------

  [TestMethod]
  public void AddQuartzScheduler_WhenCalled_RegistersISchedulerFactory()
  {
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();

    // Act
    services.AddQuartzScheduler();
    var provider = services.BuildServiceProvider();

    // Assert
    var factory = provider.GetService<ISchedulerFactory>();
    Assert.IsNotNull(factory, "ISchedulerFactory should be registered by AddQuartzScheduler.");
  }

  [TestMethod]
  public void AddQuartzScheduler_WhenCalled_RegistersQuartzHostedService()
  {
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    var hostedServicesBefore = services.Count(sd => sd.ServiceType == typeof(IHostedService));

    // Act
    services.AddQuartzScheduler();

    // Assert — AddQuartzHostedService added at least one IHostedService descriptor
    var hostedServicesAfter = services.Count(sd => sd.ServiceType == typeof(IHostedService));
    Assert.IsTrue(
        hostedServicesAfter > hostedServicesBefore,
        "AddQuartzScheduler should register at least one IHostedService.");
  }

  // ---------------------------------------------------------------
  // ScheduleCronJobAsync — scheduling behaviour tests
  // ---------------------------------------------------------------

  private static Task<IScheduler> CreateIsolatedSchedulerAsync()
  {
    var props = new NameValueCollection
    {
      ["quartz.scheduler.instanceName"] = $"TestScheduler_{Guid.NewGuid():N}"
    };
    return new StdSchedulerFactory(props).GetScheduler();
  }

  [TestMethod]
  public async Task ScheduleCronJobAsync_ValidCron_JobAndTriggerExistInScheduler()
  {
    // Arrange
    var scheduler = await CreateIsolatedSchedulerAsync();
    await scheduler.Start();

    const string cron = "0 0 1 * * ?"; // every day at 01:00

    // Act
    await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
        scheduler, cron, cancellationToken: CancellationToken.None);

    // Assert — job was stored
    var jobKey = new JobKey(nameof(SmokeTestJob));
    Assert.IsTrue(
        await scheduler.CheckExists(jobKey),
        "Job should exist in the scheduler after ScheduleCronJobAsync.");

    // Assert — trigger is linked to the job
    var triggers = await scheduler.GetTriggersOfJob(jobKey);
    Assert.AreEqual(1, triggers.Count, "Exactly one trigger should be associated with the job.");

    await scheduler.Shutdown(waitForJobsToComplete: false);
  }

  [TestMethod]
  public async Task ScheduleCronJobAsync_WithJobDataMap_DataPropagatedToJob()
  {
    // Arrange
    var scheduler = await CreateIsolatedSchedulerAsync();
    await scheduler.Start();

    var jobData = new JobDataMap { { "key", "value" } };
    const string cron = "0 0 2 * * ?";

    // Act
    await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
        scheduler, cron, jobData, CancellationToken.None);

    // Assert — job detail carries the data map
    var jobKey = new JobKey(nameof(SmokeTestJob));
    var detail = await scheduler.GetJobDetail(jobKey);
    Assert.IsNotNull(detail);
    Assert.AreEqual("value", detail.JobDataMap["key"]);

    await scheduler.Shutdown(waitForJobsToComplete: false);
  }

  [TestMethod]
  public async Task ScheduleCronJobAsync_ValidCron_CronExpressionMatchesTrigger()
  {
    // Arrange
    var scheduler = await CreateIsolatedSchedulerAsync();
    await scheduler.Start();

    const string cron = "0 30 5 * * ?"; // 05:30 every day

    // Act
    await QuartzJobScheduler.ScheduleCronJobAsync<SmokeTestJob>(
        scheduler, cron, cancellationToken: CancellationToken.None);

    // Assert — trigger cron expression matches what was supplied
    var jobKey = new JobKey(nameof(SmokeTestJob));
    var triggers = await scheduler.GetTriggersOfJob(jobKey);
    var cronTrigger = triggers.OfType<ICronTrigger>().First();
    Assert.AreEqual(cron, cronTrigger.CronExpressionString);

    await scheduler.Shutdown(waitForJobsToComplete: false);
  }
}
