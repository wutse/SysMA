using BrokerageMonitor.Domain.Events;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Evaluates aggregate-health definitions against component progress.
/// US-049/US-050 (EP-009) provide the full implementation; this interface stub
/// is defined here so the event dispatch chain (US-028) can reference it.
/// FR-034, FR-045.
/// </summary>
public interface IAggregateHealthEvaluationService
{
    /// <summary>
    /// Called for every <see cref="ComponentStatusChanged"/> event to update
    /// the CompletedComponents list in any InProgress <c>DailyExecution</c>
    /// whose WatchedComponents includes the affected component (FR-034).
    /// </summary>
    Task UpdateComponentProgressAsync(ComponentStatusChanged evt, CancellationToken ct = default);
}
