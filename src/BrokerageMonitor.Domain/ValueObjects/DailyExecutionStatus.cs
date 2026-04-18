namespace BrokerageMonitor.Domain.ValueObjects;

public enum DailyExecutionStatus
{
    InProgress,
    Success,
    Failed,
    Missed,
    Exempted
}
