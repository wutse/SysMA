using BrokerageMonitor.Domain.Events;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Evaluates aggregate-health definitions against component progress.
/// US-049/US-050 (EP-009) provide the full implementation.
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

    /// <summary>
    /// Called at deadline time for a specific definition.
    /// Evaluates all watched components using BI-008 criteria, transitions the
    /// <c>DailyExecution</c> to its terminal state (Success/Failed/Exempted),
    /// sends notifications according to <c>SendOnFailure</c>, and writes a
    /// <c>NotificationInboxItem</c> regardless (FR-020).
    /// FR-014, FR-016, FR-045, BI-006, BI-008, BI-013.
    /// </summary>
    Task EvaluateDefinitionAsync(Guid definitionId, CancellationToken ct = default);
}
