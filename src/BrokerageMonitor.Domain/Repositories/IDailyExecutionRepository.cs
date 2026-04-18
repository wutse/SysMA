using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Repositories;

public interface IDailyExecutionRepository
{
    Task<DailyExecution?> GetByDefinitionAndDateAsync(
        Guid definitionId, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<DailyExecution>> GetByDateAsync(DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<DailyExecution>> QueryHistoryAsync(
        Guid? definitionId, string? systemId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task AddAsync(DailyExecution execution, CancellationToken ct = default);

    /// <summary>
    /// Updates to a terminal status only. BI-013: terminal state cannot be overwritten.
    /// </summary>
    Task UpdateStatusAsync(
        Guid executionId,
        DailyExecutionStatus status,
        DateTimeOffset evaluatedAt,
        IReadOnlyList<string>? failedComponents,
        DateTimeOffset? notificationSentAt,
        CancellationToken ct = default);

    /// <summary>
    /// Event-driven progress tracking (FR-034).
    /// </summary>
    Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default);

    /// <summary>
    /// 30-day data retention (FR-024).
    /// </summary>
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
