using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Infrastructure.Notifications;

/// <summary>
/// Concrete <see cref="IAuditLogger"/> that persists audit entries via
/// <see cref="IAuditLogRepository"/>. Registered in the Infrastructure layer
/// so that the Application layer's <c>NullAuditLogger</c> stub is overridden.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IAuditLogRepository _repository;

    public AuditLogger(IAuditLogRepository repository) => _repository = repository;

    /// <inheritdoc />
    public Task LogStatusChangedAsync(
        string systemId,
        string componentId,
        ComponentStatus previous,
        ComponentStatus current,
        DateTimeOffset occurredAt,
        CancellationToken ct = default)
    {
        var entry = new AuditLogEntry(
            Guid.NewGuid(),
            systemId,
            componentId,
            ActionType: $"StatusChanged:{previous}->{current}",
            OperatorName: "System",
            Reason: null,
            OccurredAt: occurredAt);

        return _repository.AddAsync(entry, ct);
    }

    /// <inheritdoc />
    public Task LogOperatorActionAsync(
        string systemId,
        string? componentId,
        string actionType,
        string operatorName,
        string? reason,
        DateTimeOffset occurredAt,
        CancellationToken ct = default)
    {
        var entry = new AuditLogEntry(
            Guid.NewGuid(),
            systemId,
            componentId,
            actionType,
            operatorName,
            reason,
            occurredAt);

        return _repository.AddAsync(entry, ct);
    }
}
