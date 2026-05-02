using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ReadModels;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Tests.Scheduling;

[TestClass]
public sealed class DataRetentionJobTests
{
    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static DataRetentionJob CreateJob(
        FakeExecutionHistoryRepository? executionHistory = null,
        FakeAuditLogRepository? auditLog = null,
        FakeNotificationInboxRepository? notificationInbox = null,
        FakeDailyExecutionRepository? dailyExecution = null) =>
        new(
            executionHistory ?? new FakeExecutionHistoryRepository(),
            auditLog ?? new FakeAuditLogRepository(),
            notificationInbox ?? new FakeNotificationInboxRepository(),
            dailyExecution ?? new FakeDailyExecutionRepository(),
            TimeProvider.System,
            NullLogger<DataRetentionJob>.Instance);

    // ---------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task Execute_DefaultRetentionDays_CallsAllRepositories()
    {
        // Arrange
        var executionHistory = new FakeExecutionHistoryRepository();
        var auditLog = new FakeAuditLogRepository();
        var inbox = new FakeNotificationInboxRepository();
        var dailyExec = new FakeDailyExecutionRepository();

        var job = CreateJob(executionHistory, auditLog, inbox, dailyExec);
        var context = new FakeJobExecutionContext();

        // Act
        await job.Execute(context);

        // Assert
        Assert.IsTrue(executionHistory.DeleteCalled, "ExecutionHistory.DeleteOlderThanAsync should be called.");
        Assert.IsTrue(auditLog.DeleteCalled, "AuditLog.DeleteOlderThanAsync should be called.");
        Assert.IsTrue(inbox.DeleteCalled, "NotificationInbox.DeleteOlderThanAsync should be called.");
        Assert.IsTrue(dailyExec.DeleteCalled, "DailyExecutions.DeleteOlderThanAsync should be called.");
    }

    [TestMethod]
    public async Task Execute_WithRetentionDaysInJobData_UsesThatValue()
    {
        // Arrange
        var executionHistory = new FakeExecutionHistoryRepository();
        var job = CreateJob(executionHistory);

        var context = new FakeJobExecutionContext();
        context.SetRetentionDays(7);

        // Act
        await job.Execute(context);

        // Assert — cutoff should be approximately 7 days ago
        Assert.IsTrue(executionHistory.DeleteCalled);
        var expectedCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var actualCutoff = executionHistory.LastCutoff;
        // Allow ±5 second tolerance
        Assert.IsLessThan(5.0, Math.Abs((actualCutoff - expectedCutoff).TotalSeconds),
            $"Cutoff {actualCutoff} should be ~7 days ago ({expectedCutoff}).");
    }

    [TestMethod]
    public async Task Execute_WithoutRetentionDaysInJobData_UsesDefaultOf30Days()
    {
        // Arrange
        var executionHistory = new FakeExecutionHistoryRepository();
        var job = CreateJob(executionHistory);
        var context = new FakeJobExecutionContext(); // no RetentionDays key

        // Act
        await job.Execute(context);

        // Assert
        var expectedCutoff = DateTimeOffset.UtcNow.AddDays(-DataRetentionJob.DefaultRetentionDays);
        var actualCutoff = executionHistory.LastCutoff;
        Assert.IsLessThan(5.0, Math.Abs((actualCutoff - expectedCutoff).TotalSeconds),
            "Default retention should be 30 days.");
    }

    [TestMethod]
    public async Task Execute_WhenRepositoryThrows_ContinuesWithOtherRepositories()
    {
        // Arrange — execution history throws; others should still be called
        var throwingRepo = new FakeExecutionHistoryRepository { ThrowOnDelete = true };
        var auditLog = new FakeAuditLogRepository();
        var inbox = new FakeNotificationInboxRepository();
        var dailyExec = new FakeDailyExecutionRepository();

        var job = CreateJob(throwingRepo, auditLog, inbox, dailyExec);
        var context = new FakeJobExecutionContext();

        // Act — should not throw even if one repository fails
        await job.Execute(context);

        // Assert — the other repositories should still have been called
        Assert.IsTrue(auditLog.DeleteCalled, "AuditLog should be cleaned up even when ExecutionHistory throws.");
        Assert.IsTrue(inbox.DeleteCalled, "NotificationInbox should be cleaned up even when ExecutionHistory throws.");
        Assert.IsTrue(dailyExec.DeleteCalled, "DailyExecutions should be cleaned up even when ExecutionHistory throws.");
    }

    // ---------------------------------------------------------------
    // Fake collaborators
    // ---------------------------------------------------------------

    internal sealed class FakeExecutionHistoryRepository : IExecutionHistoryRepository
    {
        public bool DeleteCalled { get; private set; }
        public DateTimeOffset LastCutoff { get; private set; }
        public bool ThrowOnDelete { get; set; }

        public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("Simulated repository failure.");
            }
            DeleteCalled = true;
            LastCutoff = cutoff;
            return Task.CompletedTask;
        }

        public Task AddAsync(ExecutionHistoryEntry entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExecutionHistoryEntry>> QueryAsync(
            string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionHistoryEntry>>([]);
    }

    internal sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public bool DeleteCalled { get; private set; }

        public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task AddAsync(AuditLogEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
            string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditLogEntry>>([]);
    }

    internal sealed class FakeNotificationInboxRepository : INotificationInboxRepository
    {
        public bool DeleteCalled { get; private set; }

        public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task AddAsync(NotificationInboxItem item, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationInboxItem>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationInboxItem>>([]);
        public Task MarkAsReadAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class FakeDailyExecutionRepository : IDailyExecutionRepository
    {
        public bool DeleteCalled { get; private set; }

        public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task<DailyExecution?> GetByDefinitionAndDateAsync(Guid definitionId, DateOnly date, CancellationToken ct = default) =>
            Task.FromResult<DailyExecution?>(null);
        public Task<IReadOnlyList<DailyExecution>> GetByDateAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DailyExecution>>([]);
        public Task<IReadOnlyList<DailyExecution>> QueryHistoryAsync(Guid? definitionId, string? systemId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DailyExecution>>([]);
        public Task AddAsync(DailyExecution execution, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateStatusAsync(Guid executionId, DailyExecutionStatus status, DateTimeOffset evaluatedAt, IReadOnlyList<string>? failedComponents, DateTimeOffset? notificationSentAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ---------------------------------------------------------------
    // Fake IJobExecutionContext
    // ---------------------------------------------------------------

    private sealed class FakeJobExecutionContext : IJobExecutionContext
    {
        private readonly JobDataMap _jobDataMap = new();

        public void SetRetentionDays(int days)
        {
            _jobDataMap[DataRetentionJob.RetentionDaysKey] = days;
        }

        public JobDataMap MergedJobDataMap => _jobDataMap;
        public CancellationToken CancellationToken => CancellationToken.None;

        // Remaining members are not used by DataRetentionJob — return safe defaults.
        public IScheduler Scheduler => null!;
        public ITrigger Trigger => null!;
        public ICalendar? Calendar => null;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => null!;
        public int RefireCount => 0;
        public JobDataMap JobDetail_JobDataMap => _jobDataMap;
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
