namespace BrokerageMonitor.Application.Messaging;

/// <summary>
/// DTO representing a ZeroMQ heartbeat or status-update message payload (Design Doc §6.1).
/// messageType values: "Heartbeat" | "StatusUpdate"
/// </summary>
public sealed record HeartbeatMessage(
    string MessageType,
    string SystemId,
    string ComponentId,
    DateTimeOffset Timestamp,
    string Status,
    string? Message,
    IReadOnlyList<SubIndicatorPayload>? SubIndicators);

/// <summary>Sub-indicator entry within a <see cref="HeartbeatMessage"/>.</summary>
public sealed record SubIndicatorPayload(
    string Name,
    string Status,
    MetricPayload? Metric);

/// <summary>Optional metric attached to a <see cref="SubIndicatorPayload"/>.</summary>
public sealed record MetricPayload(string Label, decimal Value);
