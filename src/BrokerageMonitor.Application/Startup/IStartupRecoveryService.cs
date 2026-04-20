namespace BrokerageMonitor.Application.Startup;

/// <summary>
/// Performs station startup recovery: loads the last-known component states
/// from persistence, transitions Running ScheduledJob components to Warning
/// (FR-033), and arms heartbeat timers for Service components.
/// Called from an IHostedService.StartAsync in the Infrastructure layer.
/// US-030, FR-033.
/// </summary>
public interface IStartupRecoveryService
{
    Task RecoverAsync(CancellationToken ct = default);
}
