using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Computes a system-level rolled-up status from its component statuses.
/// Pure function — no side effects or I/O. US-027, FR-001.
/// </summary>
public interface IStateRollupService
{
    /// <summary>
    /// Returns the highest-severity <see cref="ComponentStatus"/> from the supplied collection.
    /// Severity order (descending): Lost &gt; Error &gt; Warning &gt; Unknown &gt; Stopped &gt; Idle &gt; Running &gt; Normal.
    /// Returns <see cref="ComponentStatus.Normal"/> for an empty collection.
    /// </summary>
    ComponentStatus ComputeSystemStatus(IEnumerable<ComponentStatus> componentStatuses);
}
