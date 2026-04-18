using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing a daily execution instance for a health monitor definition.
/// FR-042, FR-043, FR-044, FR-045, BI-012, BI-013
/// </summary>
public sealed class DailyExecution
{
    private static readonly IReadOnlySet<DailyExecutionStatus> TerminalStatuses =
        new HashSet<DailyExecutionStatus>
        {
            DailyExecutionStatus.Success,
            DailyExecutionStatus.Failed,
            DailyExecutionStatus.Missed,
            DailyExecutionStatus.Exempted
        };

    private readonly List<string> _completedComponents = [];
    private readonly List<string> _failedComponents = [];

    public Guid ExecutionId { get; private init; }
    public Guid DefinitionId { get; private init; }
    public string SystemId { get; private init; }
    public DateOnly ExecutionDate { get; private init; }
    public DailyExecutionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? EvaluatedAt { get; private set; }
    public IReadOnlyList<string> CompletedComponents => _completedComponents.AsReadOnly();
    public IReadOnlyList<string> FailedComponents => _failedComponents.AsReadOnly();
    public string? MissedReason { get; private set; }
    public DateTimeOffset? NotificationSentAt { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);

    // Required for Dapper materialization
    private DailyExecution()
    {
        SystemId = null!;
    }

    public DailyExecution(
        Guid executionId,
        Guid definitionId,
        string systemId,
        DateOnly executionDate,
        DailyExecutionStatus initialStatus = DailyExecutionStatus.InProgress,
        string? missedReason = null)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("ExecutionId cannot be empty.", nameof(executionId));

        if (definitionId == Guid.Empty)
            throw new ArgumentException("DefinitionId cannot be empty.", nameof(definitionId));

        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(systemId));

        ExecutionId = executionId;
        DefinitionId = definitionId;
        SystemId = systemId;
        ExecutionDate = executionDate;
        Status = initialStatus;
        CreatedAt = DateTimeOffset.UtcNow;
        MissedReason = missedReason;
    }

    /// <summary>
    /// Records a component as completed. Idempotent — duplicate entries are ignored.
    /// Used for event-driven progress tracking (FR-034).
    /// </summary>
    public void AddCompletedComponent(string componentId)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        if (IsTerminal)
            return; // Terminal state — silently ignore

        if (!_completedComponents.Contains(componentId, StringComparer.OrdinalIgnoreCase))
            _completedComponents.Add(componentId);
    }

    /// <summary>
    /// Transitions to a terminal status. BI-013: once terminal, cannot be overwritten.
    /// </summary>
    public void Complete(
        DailyExecutionStatus terminalStatus,
        DateTimeOffset evaluatedAt,
        IEnumerable<string>? failedComponents = null)
    {
        if (!TerminalStatuses.Contains(terminalStatus))
            throw new ArgumentException($"Status '{terminalStatus}' is not a terminal status.", nameof(terminalStatus));

        if (IsTerminal)
            throw new InvalidOperationException($"DailyExecution '{ExecutionId}' is already in terminal state '{Status}' and cannot be overwritten (BI-013).");

        Status = terminalStatus;
        EvaluatedAt = evaluatedAt;

        if (failedComponents is not null)
            _failedComponents.AddRange(failedComponents);
    }

    public void RecordNotificationSent(DateTimeOffset sentAt) => NotificationSentAt = sentAt;
}
