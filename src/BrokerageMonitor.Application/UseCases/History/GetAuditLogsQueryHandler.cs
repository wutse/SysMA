using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.History;

/// <summary>
/// Query to retrieve audit log entries with optional system and date-range filters.
/// FR-023.
/// </summary>
/// <param name="SystemId">Optional system filter; <c>null</c> returns all systems.</param>
/// <param name="From">Inclusive lower bound (UTC).</param>
/// <param name="To">Inclusive upper bound (UTC).</param>
public sealed record GetAuditLogsQuery(
    string? SystemId,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>
/// Handles <see cref="GetAuditLogsQuery"/> by delegating to <see cref="IAuditLogRepository"/>.
/// </summary>
public sealed class GetAuditLogsQueryHandler
{
    private readonly IAuditLogRepository _auditLogRepository;

    public GetAuditLogsQueryHandler(IAuditLogRepository auditLogRepository)
        => _auditLogRepository = auditLogRepository;

    /// <summary>
    /// Returns audit log entries matching the query criteria.
    /// </summary>
    public Task<IReadOnlyList<AuditLogEntry>> HandleAsync(
        GetAuditLogsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _auditLogRepository.QueryAsync(query.SystemId, query.From, query.To, ct);
    }
}
