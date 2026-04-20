using BrokerageMonitor.Domain.Aggregates;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Thread-safe in-memory store for <see cref="ComponentState"/> objects.
/// Backed by a <see cref="Dictionary{TKey,TValue}"/> protected by a single lock.
/// Registered as Singleton in DI.
/// </summary>
public sealed class ComponentStateCache : IComponentStateCache
{
    private readonly Dictionary<string, ComponentState> _states = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ComponentState? GetState(string componentId)
    {
        lock (_lock)
        {
            return _states.GetValueOrDefault(componentId);
        }
    }

    public void SetState(ComponentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lock)
        {
            _states[state.ComponentId] = state;
        }
    }

    public IReadOnlyList<ComponentState> GetAllStates()
    {
        lock (_lock)
        {
            return [.. _states.Values];
        }
    }

    public void LoadAll(IEnumerable<ComponentState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        lock (_lock)
        {
            _states.Clear();
            foreach (var s in states)
                _states[s.ComponentId] = s;
        }
    }
}
