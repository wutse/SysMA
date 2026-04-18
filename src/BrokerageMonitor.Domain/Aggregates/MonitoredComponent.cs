using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Aggregates;

/// <summary>
/// Aggregate Root representing a monitored component within a system.
/// FR-028, FR-036, FR-038, FR-039, FR-049
/// </summary>
public sealed class MonitoredComponent
{
    public string ComponentId { get; private init; }
    public string SystemId { get; private init; }
    public string Name { get; private set; }
    public ComponentType ComponentType { get; private set; }
    public string ZeroMQTopic { get; private set; }
    public int HeartbeatTimeoutSeconds { get; private set; }

    /// <summary>
    /// Required only for ScheduledJob; defines the allowed execution window (FR-038).
    /// </summary>
    public string? CronExpression { get; private set; }

    /// <summary>
    /// Optional mail parsing rule. BI-016: SuccessKeywords and FailureKeywords must not overlap.
    /// </summary>
    public MailParsingRule? MailParsingRule { get; private set; }

    public bool IsActive { get; private set; }

    // Required for Dapper materialization
    private MonitoredComponent()
    {
        ComponentId = null!;
        SystemId = null!;
        Name = null!;
        ZeroMQTopic = null!;
    }

    public MonitoredComponent(
        string componentId,
        string systemId,
        string name,
        ComponentType componentType,
        string zeroMQTopic,
        int heartbeatTimeoutSeconds,
        string? cronExpression = null,
        MailParsingRule? mailParsingRule = null,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(componentId))
            throw new ArgumentException("ComponentId cannot be empty.", nameof(componentId));

        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("SystemId cannot be empty.", nameof(systemId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        if (string.IsNullOrWhiteSpace(zeroMQTopic))
            throw new ArgumentException("ZeroMQTopic cannot be empty.", nameof(zeroMQTopic));

        if (heartbeatTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeoutSeconds), "HeartbeatTimeoutSeconds must be positive.");

        if (componentType == ComponentType.ScheduledJob && string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required for ScheduledJob components.", nameof(cronExpression));

        ComponentId = componentId;
        SystemId = systemId;
        Name = name;
        ComponentType = componentType;
        ZeroMQTopic = zeroMQTopic;
        HeartbeatTimeoutSeconds = heartbeatTimeoutSeconds;
        CronExpression = cronExpression;
        MailParsingRule = mailParsingRule;
        IsActive = isActive;
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name cannot be empty.", nameof(newName));

        Name = newName;
    }

    public void UpdateHeartbeatTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "HeartbeatTimeoutSeconds must be positive.");

        HeartbeatTimeoutSeconds = timeoutSeconds;
    }

    public void UpdateCronExpression(string? cronExpression)
    {
        if (ComponentType == ComponentType.ScheduledJob && string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CronExpression is required for ScheduledJob components.", nameof(cronExpression));

        CronExpression = cronExpression;
    }

    public void SetMailParsingRule(MailParsingRule? rule) => MailParsingRule = rule;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
