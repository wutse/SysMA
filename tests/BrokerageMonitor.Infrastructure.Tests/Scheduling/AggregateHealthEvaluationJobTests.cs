using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Tests.Scheduling;

[TestClass]
public sealed class AggregateHealthEvaluationJobTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AggregateHealthEvaluationJob CreateJob(
        IAggregateHealthEvaluationService service) =>
        new(
            new FakeScopeFactory(service),
            NullLogger<AggregateHealthEvaluationJob>.Instance);

    // -----------------------------------------------------------------------
    // Execute — missing / invalid JobDataMap
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Execute_MissingDefinitionIdInJobDataMap_DoesNotCallService()
    {
        // Arrange — empty JobDataMap: no DefinitionId key
        var service = new FakeAggregateHealthService();
        var job = CreateJob(service);
        var context = new FakeJobContext();

        // Act
        await job.Execute(context);

        // Assert
        Assert.IsFalse(service.EvaluateCalled,
            "Service should not be called when DefinitionId is missing from JobDataMap.");
    }

    [TestMethod]
    public async Task Execute_InvalidGuidInJobDataMap_DoesNotCallService()
    {
        var service = new FakeAggregateHealthService();
        var job = CreateJob(service);
        var context = new FakeJobContext();
        context.SetData(AggregateHealthEvaluationJob.DefinitionIdKey, "not-a-valid-guid");

        await job.Execute(context);

        Assert.IsFalse(service.EvaluateCalled);
    }

    // -----------------------------------------------------------------------
    // Execute — valid definition ID
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Execute_ValidDefinitionId_CallsEvaluateDefinitionAsync()
    {
        // Arrange
        var definitionId = Guid.NewGuid();
        var service = new FakeAggregateHealthService();
        var job = CreateJob(service);
        var context = new FakeJobContext();
        context.SetData(AggregateHealthEvaluationJob.DefinitionIdKey, definitionId.ToString());

        // Act
        await job.Execute(context);

        // Assert
        Assert.IsTrue(service.EvaluateCalled);
        Assert.AreEqual(definitionId, service.LastDefinitionId);
    }

    // -----------------------------------------------------------------------
    // Execute — exception handling
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Execute_WhenServiceThrows_DoesNotRethrow()
    {
        // US-051: exceptions are logged as ERROR but must NOT propagate so that
        // Quartz continues processing other jobs.
        var service = new FakeAggregateHealthService { ThrowOnEvaluate = true };
        var job = CreateJob(service);
        var context = new FakeJobContext();
        context.SetData(AggregateHealthEvaluationJob.DefinitionIdKey, Guid.NewGuid().ToString());

        // Act — must complete without throwing
        await job.Execute(context);
    }

    // -----------------------------------------------------------------------
    // Fake collaborators
    // -----------------------------------------------------------------------

    private sealed class FakeAggregateHealthService : IAggregateHealthEvaluationService
    {
        public bool EvaluateCalled { get; private set; }
        public Guid LastDefinitionId { get; private set; }
        public bool ThrowOnEvaluate { get; set; }

        public Task EvaluateDefinitionAsync(Guid definitionId, CancellationToken ct = default)
        {
            if (ThrowOnEvaluate)
                throw new InvalidOperationException("Simulated service failure.");

            EvaluateCalled = true;
            LastDefinitionId = definitionId;
            return Task.CompletedTask;
        }

        public Task UpdateComponentProgressAsync(
            ComponentStatusChanged evt, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        private readonly IAggregateHealthEvaluationService _service;

        public FakeScopeFactory(IAggregateHealthEvaluationService service) => _service = service;

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
        private readonly IAggregateHealthEvaluationService _service;

        public FakeServiceProvider(IAggregateHealthEvaluationService service) => _service = service;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IAggregateHealthEvaluationService) ? _service : null;
    }

    private sealed class FakeJobContext : IJobExecutionContext
    {
        private readonly JobDataMap _map = new();

        public void SetData(string key, string value) => _map[key] = value;

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
