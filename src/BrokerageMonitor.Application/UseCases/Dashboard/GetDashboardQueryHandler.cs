using BrokerageMonitor.Application.DTOs;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;

namespace BrokerageMonitor.Application.UseCases.Dashboard;

/// <summary>
/// Handles <see cref="GetDashboardQuery"/>. Combines in-memory component state
/// with active system/component configuration to produce per-system summaries
/// including the rolled-up status and unacknowledged-alert flag.
/// US-029, FR-001.
/// </summary>
public sealed class GetDashboardQueryHandler
{
    private readonly IMonitoredSystemRepository _systemRepository;
    private readonly IMonitoredComponentRepository _componentRepository;
    private readonly IAlertRecordRepository _alertRepository;
    private readonly IComponentStateCache _stateCache;
    private readonly IStateRollupService _rollupService;

    public GetDashboardQueryHandler(
        IMonitoredSystemRepository systemRepository,
        IMonitoredComponentRepository componentRepository,
        IAlertRecordRepository alertRepository,
        IComponentStateCache stateCache,
        IStateRollupService rollupService)
    {
        _systemRepository = systemRepository;
        _componentRepository = componentRepository;
        _alertRepository = alertRepository;
        _stateCache = stateCache;
        _rollupService = rollupService;
    }

    public async Task<IReadOnlyList<SystemSummaryDto>> HandleAsync(
        GetDashboardQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var systems = await _systemRepository.GetAllActiveAsync(ct);
        var result = new List<SystemSummaryDto>(systems.Count);

        foreach (var system in systems)
        {
            var components = await _componentRepository.GetBySystemIdAsync(system.SystemId, ct);
            var activeComponents = components.Where(c => c.IsActive).ToList();

            var componentDtos = BuildComponentDtos(activeComponents);

            var rolledUpStatus = _rollupService.ComputeSystemStatus(
                componentDtos.Select(c => c.Status));

            bool hasAlert = await _alertRepository.HasUnacknowledgedAlertAsync(system.SystemId, ct);

            result.Add(new SystemSummaryDto(
                system.SystemId,
                system.Name,
                rolledUpStatus,
                system.IsMaintenanceActive,
                hasAlert,
                componentDtos));
        }

        return result.AsReadOnly();
    }

    private List<ComponentStatusDto> BuildComponentDtos(IEnumerable<MonitoredComponent> components)
    {
        var dtos = new List<ComponentStatusDto>();
        foreach (var component in components)
        {
            var state = _stateCache.GetState(component.ComponentId)
                ?? new ComponentState(component.ComponentId);

            dtos.Add(new ComponentStatusDto(
                component.ComponentId,
                component.Name,
                component.ComponentType,
                state.Status,
                state.LastHeartbeatAt,
                state.SubIndicators));
        }
        return dtos;
    }
}
