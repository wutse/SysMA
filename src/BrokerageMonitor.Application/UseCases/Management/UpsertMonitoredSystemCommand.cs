using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.UseCases.Management;

/// <summary>
/// Command to create or update a <see cref="Domain.Aggregates.MonitoredSystem"/>.
/// FR-031: System CRUD via the management UI.
/// </summary>
/// <param name="SystemId">Unique system identifier (used as natural key for upsert).</param>
/// <param name="Name">Display name.</param>
/// <param name="MarketSessionStart">Market session start time (local).</param>
/// <param name="MarketSessionEnd">Market session end time (local).</param>
/// <param name="AlertRecipients">Email addresses to notify on alert.</param>
/// <param name="IsActive">Whether the system participates in monitoring.</param>
public sealed record UpsertMonitoredSystemCommand(
    string SystemId,
    string Name,
    TimeOnly MarketSessionStart,
    TimeOnly MarketSessionEnd,
    IReadOnlyList<string> AlertRecipients,
    bool IsActive = true);
