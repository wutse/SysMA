using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Pure-function implementation of <see cref="IStateRollupService"/>.
/// Severity order (US-027): Lost(7) &gt; Error(6) &gt; Warning(5) &gt; Unknown(4) &gt; Stopped(3)
/// &gt; Idle(2) &gt; Running(1) &gt; Normal(0).
/// Maintenance(-1) is treated as lower than Normal and excluded from rollup.
/// FR-001.
/// </summary>
public sealed class StateRollupService : IStateRollupService
{
    /// <inheritdoc />
    public ComponentStatus ComputeSystemStatus(IEnumerable<ComponentStatus> componentStatuses)
    {
        ArgumentNullException.ThrowIfNull(componentStatuses);

        var worst = ComponentStatus.Normal;
        foreach (var status in componentStatuses)
        {
            if (GetSeverity(status) > GetSeverity(worst))
                worst = status;
        }
        return worst;
    }

    /// <summary>
    /// Returns the severity rank for <paramref name="status"/>.
    /// Maintenance is excluded from rollup (rank -1) so it never overrides operational statuses.
    /// </summary>
    internal static int GetSeverity(ComponentStatus status) => status switch
    {
        ComponentStatus.Lost => 7,
        ComponentStatus.Error => 6,
        ComponentStatus.Failed => 6,
        ComponentStatus.Warning => 5,
        ComponentStatus.Unknown => 4,
        ComponentStatus.Stopped => 3,
        ComponentStatus.Idle => 2,
        ComponentStatus.Running => 1,
        ComponentStatus.Normal => 0,
        ComponentStatus.Completed => 0,
        ComponentStatus.Maintenance => -1,
        _ => 0
    };
}
