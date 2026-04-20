using BrokerageMonitor.Domain.Events;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Evaluates whether a status change should trigger a new alert.
/// US-031 (EP-006) provides the full implementation; this interface stub
/// is defined here so the event dispatch chain (US-028) can reference it.
/// FR-010, FR-011, BI-004, BI-006, BI-007.
/// </summary>
public interface IAlertEvaluationService
{
    Task EvaluateAsync(ComponentStatusChanged evt, CancellationToken ct = default);
}
