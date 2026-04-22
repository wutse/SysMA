namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Creates <c>DailyExecution</c> instances for all active
/// <c>HealthMonitorDefinition</c> records whose schedule matches the target date.
/// US-048 / US-052 (EP-009).
/// FR-042, FR-044, BI-012, BI-015.
/// </summary>
public interface IDailyExecutionCreatorService
{
    /// <summary>
    /// Creates <c>InProgress</c> <c>DailyExecution</c> instances for every active
    /// definition whose schedule matches <paramref name="date"/>.
    /// Already-existing executions for the same definition+date are silently skipped (BI-012).
    /// After creation, watched <c>ScheduledJob</c> components are reset to <c>Idle</c>.
    /// </summary>
    Task CreateForDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Called on station startup. For each active definition whose schedule matches today:
    /// <list type="bullet">
    ///   <item>If deadline has not yet passed → creates an <c>InProgress</c> execution.</item>
    ///   <item>If deadline has already passed → creates a <c>Missed</c> execution with
    ///         <c>MissedReason = "站台未運行"</c>.</item>
    ///   <item>If an execution already exists → skips (BI-012).</item>
    /// </list>
    /// FR-044, BI-012, BI-015.
    /// </summary>
    Task RecoverTodayAsync(CancellationToken ct = default);
}
