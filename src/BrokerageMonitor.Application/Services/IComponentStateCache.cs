using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// In-memory cache of component runtime states.
/// Populated on startup from SQLite and updated on every heartbeat.
/// Used to avoid per-request database reads for hot dashboard data (US-029, US-030).
/// </summary>
public interface IComponentStateCache
{
    ComponentState? GetState(string componentId);
    void SetState(ComponentState state);
    IReadOnlyList<ComponentState> GetAllStates();
    void LoadAll(IEnumerable<ComponentState> states);
}
