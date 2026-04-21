using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Command to create or update a <see cref="Domain.Aggregates.MonitoredComponent"/>.
/// FR-031: Component CRUD via the management UI.
/// </summary>
/// <param name="ComponentId">Unique component identifier (natural key for upsert).</param>
/// <param name="SystemId">Owning system identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="ComponentType">Service or ScheduledJob.</param>
/// <param name="ZeroMQTopic">ZeroMQ subscription topic.</param>
/// <param name="HeartbeatTimeoutSeconds">Seconds before the component is considered lost.</param>
/// <param name="CronExpression">Required for ScheduledJob; defines the execution window.</param>
/// <param name="MailParsingRule">Optional mail parsing rule (BI-016).</param>
/// <param name="IsActive">Whether this component participates in monitoring.</param>
public sealed record UpsertMonitoredComponentCommand(
    string ComponentId,
    string SystemId,
    string Name,
    ComponentType ComponentType,
    string ZeroMQTopic,
    int HeartbeatTimeoutSeconds,
    string? CronExpression = null,
    MailParsingRule? MailParsingRule = null,
    bool IsActive = true);
