namespace BrokerageMonitor.Domain.ValueObjects;

public enum ComponentStatus
{
    Unknown,
    Normal,
    Warning,
    Error,
    Lost,
    Stopped,
    Idle,
    Running,
    Completed,
    Failed,
    Maintenance
}
