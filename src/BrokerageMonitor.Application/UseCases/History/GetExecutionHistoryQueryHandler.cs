using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.History;

/// <summary>
/// Query to retrieve execution history entries with optional system and date-range filters.
/// FR-021, FR-022.
/// </summary>
/// <param name="SystemId">Optional system filter; <c>null</c> returns all systems.</param>
/// <param name="From">Inclusive lower bound (UTC).</param>
/// <param name="To">Inclusive upper bound (UTC).</param>
public sealed record GetExecutionHistoryQuery(
    string? SystemId,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>
/// Handles <see cref="GetExecutionHistoryQuery"/> by delegating to <see cref="IExecutionHistoryRepository"/>.
/// </summary>
public sealed class GetExecutionHistoryQueryHandler
{
    private readonly IExecutionHistoryRepository _historyRepository;

    public GetExecutionHistoryQueryHandler(IExecutionHistoryRepository historyRepository)
        => _historyRepository = historyRepository;

    /// <summary>
    /// Returns execution history entries matching the query criteria.
    /// </summary>
    public Task<IReadOnlyList<ExecutionHistoryEntry>> HandleAsync(
        GetExecutionHistoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _historyRepository.QueryAsync(query.SystemId, query.From, query.To, ct);
    }
}
