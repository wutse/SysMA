using BrokerageMonitor.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BrokerageMonitor.Infrastructure.Scheduling;

/// <summary>
/// Quartz job triggered at each <c>HealthMonitorDefinition.DeadlineTime</c>.
/// Delegates to <see cref="IAggregateHealthEvaluationService.EvaluateDefinitionAsync"/>
/// for the specified definition.
/// US-051 (EP-009). FR-045.
///
/// The job is dynamically scheduled at startup (and on definition upsert) with a
/// <c>JobDataMap</c> key <see cref="DefinitionIdKey"/> containing the target
/// <see cref="Guid"/> as a string.
/// </summary>
[DisallowConcurrentExecution]
public sealed class AggregateHealthEvaluationJob : IJob
{
    /// <summary>JobDataMap key for the target definition ID.</summary>
    public const string DefinitionIdKey = "DefinitionId";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AggregateHealthEvaluationJob> _logger;

    public AggregateHealthEvaluationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AggregateHealthEvaluationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        if (!context.MergedJobDataMap.ContainsKey(DefinitionIdKey) ||
            !Guid.TryParse(context.MergedJobDataMap.GetString(DefinitionIdKey), out var definitionId))
        {
            _logger.LogError(
                "AggregateHealthEvaluationJob: missing or invalid '{Key}' in JobDataMap.",
                DefinitionIdKey);
            return;
        }

        _logger.LogInformation(
            "AggregateHealthEvaluationJob started for definition {DefinitionId}.", definitionId);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAggregateHealthEvaluationService>();
            await service.EvaluateDefinitionAsync(definitionId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "AggregateHealthEvaluationJob completed for definition {DefinitionId}.", definitionId);
        }
        catch (Exception ex)
        {
            // Log ERROR but do not rethrow — Quartz must continue processing other jobs
            _logger.LogError(ex,
                "AggregateHealthEvaluationJob failed for definition {DefinitionId}.", definitionId);
        }
    }
}
