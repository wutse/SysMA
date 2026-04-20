using BrokerageMonitor.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Quartz job that enforces the 30-day data-retention policy (FR-024, US-025).
/// Scheduled to run daily at 01:00 from the composition root.
/// Deletes records older than <c>RetentionDays</c> (default 30) from:
/// <list type="bullet">
///   <item><description>ExecutionHistory</description></item>
///   <item><description>AuditLogs</description></item>
///   <item><description>NotificationInbox</description></item>
///   <item><description>DailyExecutions</description></item>
/// </list>
/// Logs the number of deleted records per table after each run.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DataRetentionJob : IJob
{
    /// <summary>
    /// JobDataMap key for the retention period in days.
    /// Defaults to 30 days if not present in the job data map.
    /// </summary>
    public const string RetentionDaysKey = "RetentionDays";
    public const int DefaultRetentionDays = 30;

    private readonly IExecutionHistoryRepository _executionHistory;
    private readonly IAuditLogRepository _auditLog;
    private readonly INotificationInboxRepository _notificationInbox;
    private readonly IDailyExecutionRepository _dailyExecution;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(
        IExecutionHistoryRepository executionHistory,
        IAuditLogRepository auditLog,
        INotificationInboxRepository notificationInbox,
        IDailyExecutionRepository dailyExecution,
        ILogger<DataRetentionJob> logger)
    {
        _executionHistory = executionHistory;
        _auditLog = auditLog;
        _notificationInbox = notificationInbox;
        _dailyExecution = dailyExecution;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        var retentionDays = context.MergedJobDataMap.ContainsKey(RetentionDaysKey)
            ? context.MergedJobDataMap.GetInt(RetentionDaysKey)
            : DefaultRetentionDays;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        _logger.LogInformation(
            "DataRetentionJob starting. Cutoff: {Cutoff} (retaining {Days} days).",
            cutoff,
            retentionDays);

        await DeleteWithLoggingAsync(
            "ExecutionHistory",
            () => _executionHistory.DeleteOlderThanAsync(cutoff, ct)).ConfigureAwait(false);

        await DeleteWithLoggingAsync(
            "AuditLogs",
            () => _auditLog.DeleteOlderThanAsync(cutoff, ct)).ConfigureAwait(false);

        await DeleteWithLoggingAsync(
            "NotificationInbox",
            () => _notificationInbox.DeleteOlderThanAsync(cutoff, ct)).ConfigureAwait(false);

        await DeleteWithLoggingAsync(
            "DailyExecutions",
            () => _dailyExecution.DeleteOlderThanAsync(cutoff, ct)).ConfigureAwait(false);

        _logger.LogInformation("DataRetentionJob completed.");
    }

    private async Task DeleteWithLoggingAsync(string tableName, Func<Task> deleteFunc)
    {
        try
        {
            await deleteFunc().ConfigureAwait(false);
            _logger.LogInformation("DataRetentionJob: deleted records from {Table}.", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataRetentionJob: error deleting records from {Table}.", tableName);
        }
    }
}
