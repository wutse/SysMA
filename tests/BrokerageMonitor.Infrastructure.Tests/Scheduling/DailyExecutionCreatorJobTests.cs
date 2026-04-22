using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Tests.Scheduling;

[TestClass]
public sealed class DailyExecutionCreatorJobTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static DailyExecutionCreatorJob CreateJob(IDailyExecutionCreatorService service) =>
        new(
            new FakeScopeFactory(service),
            NullLogger<DailyExecutionCreatorJob>.Instance);

    // -----------------------------------------------------------------------
    // Execute — success path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Execute_CallsCreateForDateAsync_WithTodaysDate()
    {
        // Arrange
        var service = new FakeDailyExecutionCreatorService();
        var job = CreateJob(service);
        var context = new FakeJobContext();

        // Act
        await job.Execute(context);

        // Assert
        Assert.IsTrue(service.CreateForDateCalled, "CreateForDateAsync should be called.");
        Assert.AreEqual(DateOnly.FromDateTime(DateTime.Today), service.LastDate);
    }

    // -----------------------------------------------------------------------
    // Execute — exception handling
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Execute_WhenServiceThrows_ThrowsJobExecutionException()
    {
        // DailyExecutionCreatorJob wraps unhandled exceptions in JobExecutionException
        // so Quartz records a failed fire without silently swallowing the error.
        var service = new FakeDailyExecutionCreatorService { ThrowOnCreate = true };
        var job = CreateJob(service);

        await Assert.ThrowsExactlyAsync<JobExecutionException>(
            () => job.Execute(new FakeJobContext()));
    }

    [TestMethod]
    public async Task Execute_WhenServiceThrows_JobExecutionException_HasRefireImmediatelyFalse()
    {
        var service = new FakeDailyExecutionCreatorService { ThrowOnCreate = true };
        var job = CreateJob(service);

        var ex = await Assert.ThrowsExactlyAsync<JobExecutionException>(
            () => job.Execute(new FakeJobContext()));

        Assert.IsFalse(ex.RefireImmediately,
            "RefireImmediately must be false to prevent an infinite retry loop.");
    }

    // -----------------------------------------------------------------------
    // Fake collaborators
    // -----------------------------------------------------------------------

    private sealed class FakeDailyExecutionCreatorService : IDailyExecutionCreatorService
    {
        public bool CreateForDateCalled { get; private set; }
        public DateOnly LastDate { get; private set; }
        public bool ThrowOnCreate { get; set; }

        public Task CreateForDateAsync(DateOnly date, CancellationToken ct = default)
        {
            if (ThrowOnCreate)
                throw new InvalidOperationException("Simulated service failure.");

            CreateForDateCalled = true;
            LastDate = date;
            return Task.CompletedTask;
        }

        public Task RecoverTodayAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IDailyExecutionCreatorService _service;

        public FakeScopeFactory(IDailyExecutionCreatorService service) => _service = service;

        public IServiceScope CreateScope() =>
            new FakeServiceScope(new FakeServiceProvider(_service));
    }

    private sealed class FakeServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }

        public FakeServiceScope(IServiceProvider provider) => ServiceProvider = provider;

        public void Dispose() { }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly IDailyExecutionCreatorService _service;

        public FakeServiceProvider(IDailyExecutionCreatorService service) => _service = service;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IDailyExecutionCreatorService) ? _service : null;
    }

    private sealed class FakeJobContext : IJobExecutionContext
    {
        private readonly JobDataMap _map = new();

        public JobDataMap MergedJobDataMap => _map;
        public CancellationToken CancellationToken => CancellationToken.None;

        // Unused members — return safe defaults
        public IScheduler Scheduler => null!;
        public ITrigger Trigger => null!;
        public ICalendar? Calendar => null;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => null!;
        public int RefireCount => 0;
        public JobDataMap JobDetail_JobDataMap => _map;
        public IJobDetail JobDetail => null!;
        public IJob JobInstance => null!;
        public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc => null;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public DateTimeOffset? NextFireTimeUtc => null;
        public string FireInstanceId => Guid.NewGuid().ToString();
        public object? Result { get; set; }
        public TimeSpan JobRunTime => TimeSpan.Zero;
        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }
}
