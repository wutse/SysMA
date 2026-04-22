using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

// ---------------------------------------------------------------------------
// Test doubles (prefixed AHE_ to avoid cross-file naming conflicts)
// ---------------------------------------------------------------------------

internal sealed class AHE_DefinitionRepo : IHealthMonitorDefinitionRepository
{
    private readonly Dictionary<Guid, HealthMonitorDefinition> _store = [];

    public void Add(HealthMonitorDefinition def) => _store[def.DefinitionId] = def;

    public Task<HealthMonitorDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
            [.. _store.Values.Where(d => d.IsActive)]);

    public Task<IReadOnlyList<HealthMonitorDefinition>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HealthMonitorDefinition>>(
            [.. _store.Values.Where(d => d.SystemId == systemId)]);

    public Task UpsertAsync(HealthMonitorDefinition def, CancellationToken ct = default)
    {
        _store[def.DefinitionId] = def;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid definitionId, CancellationToken ct = default)
    {
        _store.Remove(definitionId);
        return Task.CompletedTask;
    }
}

internal sealed class AHE_ExecutionRepo : IDailyExecutionRepository
{
    private readonly List<DailyExecution> _store = [];

    public List<DailyExecution> Store => _store;

    public DailyExecutionStatus? UpdatedStatus { get; private set; }
    public IReadOnlyList<string>? UpdatedFailedComponents { get; private set; }

    public void Add(DailyExecution execution) => _store.Add(execution);

    public Task<DailyExecution?> GetByDefinitionAndDateAsync(
        Guid definitionId, DateOnly date, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(e =>
            e.DefinitionId == definitionId && e.ExecutionDate == date));

    public Task<IReadOnlyList<DailyExecution>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DailyExecution>>([.. _store.Where(e => e.ExecutionDate == date)]);

    public Task<IReadOnlyList<DailyExecution>> QueryHistoryAsync(
        Guid? definitionId, string? systemId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DailyExecution>>([]);

    public Task AddAsync(DailyExecution execution, CancellationToken ct = default)
    {
        _store.Add(execution);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(
        Guid executionId,
        DailyExecutionStatus status,
        DateTimeOffset evaluatedAt,
        IReadOnlyList<string>? failedComponents,
        DateTimeOffset? notificationSentAt,
        CancellationToken ct = default)
    {
        UpdatedStatus = status;
        UpdatedFailedComponents = failedComponents;
        return Task.CompletedTask;
    }

    public Task AddCompletedComponentAsync(Guid executionId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AHE_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = [];

    public void Add(MonitoredSystem system) => _store[system.SystemId] = system;

    public Task<MonitoredSystem?> GetByIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(systemId));

    public Task<IReadOnlyList<MonitoredSystem>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredSystem>>([.. _store.Values.Where(s => s.IsActive)]);

    public Task UpsertAsync(MonitoredSystem system, CancellationToken ct = default)
    {
        _store[system.SystemId] = system;
        return Task.CompletedTask;
    }

    public Task SetMaintenanceModeAsync(string systemId, bool active, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AHE_ComponentStateRepo : IComponentStateRepository
{
    private readonly Dictionary<string, ComponentState> _store = new(StringComparer.OrdinalIgnoreCase);

    public void Add(ComponentState state) => _store[state.ComponentId] = state;

    public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(componentId));

    public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
        IEnumerable<string> componentIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>(
            [.. componentIds.Select(id => _store.GetValueOrDefault(id)).OfType<ComponentState>()]);

    public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ComponentState>>([.. _store.Values]);

    public Task UpsertAsync(ComponentState state, CancellationToken ct = default)
    {
        _store[state.ComponentId] = state;
        return Task.CompletedTask;
    }
}

internal sealed class AHE_InboxRepo : INotificationInboxRepository
{
    public List<NotificationInboxItem> Store { get; } = [];

    public Task AddAsync(NotificationInboxItem item, CancellationToken ct = default)
    {
        Store.Add(item);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationInboxItem>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NotificationInboxItem>>(Store.AsReadOnly());

    public Task MarkAsReadAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AHE_EmailService : IEmailNotificationService
{
    public int SendAlertCallCount { get; private set; }
    public int SendHealthSummaryCallCount { get; private set; }
    public Exception? ThrowOnHealthSummary { get; set; }

    public Task SendAlertAsync(AlertEmailRequest request, CancellationToken ct = default)
    {
        SendAlertCallCount++;
        return Task.CompletedTask;
    }

    public Task SendHealthSummaryAsync(HealthSummaryEmailRequest request, CancellationToken ct = default)
    {
        if (ThrowOnHealthSummary is not null) throw ThrowOnHealthSummary;
        SendHealthSummaryCallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class AHE_TeamsService : ITeamsNotificationService
{
    public int CallCount { get; private set; }
    public Exception? ThrowOn { get; set; }

    public Task SendHealthSummaryAsync(
        HealthSummaryEmailRequest request,
        string webhookUrl,
        CancellationToken ct = default)
    {
        if (ThrowOn is not null) throw ThrowOn;
        CallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class AHE_Realtime : IRealtimeNotificationService
{
    public List<Guid> NotifiedExecutionIds { get; } = [];

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
    {
        NotifiedExecutionIds.Add(executionId);
        return Task.CompletedTask;
    }
}

internal sealed class AHE_Broadcaster : IMonitorBroadcaster
{
#pragma warning disable CS0067 // unused events on fake
    public event Action<string, string, ComponentStatus>? ComponentStatusUpdated;
    public event Action<string, string>? AlertTriggered;
    public event Action<string>? AlertAcknowledged;
    public event Action<string, bool>? MaintenanceModeChanged;
    public event Action<Guid, Guid, NotificationType>? HealthNotificationReceived;
#pragma warning restore CS0067

    public List<(Guid DefinitionId, Guid ExecutionId, NotificationType Type)> HealthPublished { get; } = [];

    public void PublishComponentStatusChanged(string componentId, string systemId, ComponentStatus newStatus) { }
    public void PublishAlertTriggered(string systemId, string componentId) { }
    public void PublishAlertAcknowledged(string systemId) { }
    public void PublishMaintenanceModeChanged(string systemId, bool isActive) { }

    public void PublishHealthNotificationReceived(
        Guid definitionId, Guid executionId, NotificationType notificationType)
        => HealthPublished.Add((definitionId, executionId, notificationType));
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AggregateHealthEvaluationServiceTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static readonly MarketSessionWindow DefaultSession =
        new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    private static MonitoredSystem MakeSystem(
        string systemId = "SYS",
        bool maintenanceActive = false)
    {
        var system = new MonitoredSystem(systemId, "Test System", DefaultSession);
        if (maintenanceActive)
            system.ActivateMaintenance("operator");
        return system;
    }

    private static HealthMonitorDefinition MakeDefinition(
        Guid? definitionId = null,
        string systemId = "SYS",
        IEnumerable<WatchedComponent>? watchedComponents = null,
        bool sendOnFailure = true,
        IEnumerable<EmailAddress>? emailRecipients = null,
        string? teamsWebhookUrl = null)
    {
        return new HealthMonitorDefinition(
            definitionId: definitionId ?? Guid.NewGuid(),
            systemId: systemId,
            name: "Test Def",
            deadlineTime: new TimeOnly(18, 0),
            schedule: new HealthRuleSchedule(ScheduleType.Daily),
            watchedComponents: watchedComponents ?? [new WatchedComponent("COMP-001", ComponentType.ScheduledJob)],
            emailRecipients: emailRecipients,
            teamsWebhookUrl: teamsWebhookUrl,
            sendOnFailure: sendOnFailure);
    }

    private static DailyExecution MakeInProgressExecution(Guid definitionId, string systemId = "SYS")
    {
        return new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: definitionId,
            systemId: systemId,
            executionDate: DateOnly.FromDateTime(DateTime.Today));
    }

    private (
        AggregateHealthEvaluationService Sut,
        AHE_DefinitionRepo Definitions,
        AHE_ExecutionRepo Executions,
        AHE_SystemRepo Systems,
        AHE_ComponentStateRepo ComponentStates,
        AHE_InboxRepo Inbox,
        AHE_EmailService Email,
        AHE_TeamsService Teams,
        AHE_Realtime Realtime,
        AHE_Broadcaster Broadcaster) BuildSut()
    {
        var definitions = new AHE_DefinitionRepo();
        var executions = new AHE_ExecutionRepo();
        var systems = new AHE_SystemRepo();
        var componentStates = new AHE_ComponentStateRepo();
        var inbox = new AHE_InboxRepo();
        var email = new AHE_EmailService();
        var teams = new AHE_TeamsService();
        var realtime = new AHE_Realtime();
        var broadcaster = new AHE_Broadcaster();

        var sut = new AggregateHealthEvaluationService(
            definitions,
            executions,
            systems,
            componentStates,
            inbox,
            email,
            teams,
            realtime,
            broadcaster,
            NullLogger<AggregateHealthEvaluationService>.Instance);

        return (sut, definitions, executions, systems, componentStates, inbox, email, teams, realtime, broadcaster);
    }

    // ── UpdateComponentProgressAsync ─────────────────────────────────────────

    [TestMethod]
    public async Task UpdateComponentProgressAsync_ScheduledJobCompleted_RecordsProgress()
    {
        // Arrange
        var (sut, definitions, executions, _, _, _, _, _, realtime, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
        definitions.Add(def);

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var evt = new ComponentStatusChanged(
            ComponentId: componentId,
            SystemId: "SYS",
            PreviousStatus: ComponentStatus.Running,
            NewStatus: ComponentStatus.Completed,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await sut.UpdateComponentProgressAsync(evt);

        // Assert
        Assert.Contains(componentId, execution.CompletedComponents, StringComparer.OrdinalIgnoreCase);
        Assert.IsNotEmpty(realtime.NotifiedExecutionIds);
    }

    [TestMethod]
    public async Task UpdateComponentProgressAsync_ServiceNormal_RecordsProgress()
    {
        // Arrange
        var (sut, definitions, executions, _, _, _, _, _, _, _) = BuildSut();
        const string componentId = "SVC-001";
        var def = MakeDefinition(watchedComponents: [new WatchedComponent(componentId, ComponentType.Service)]);
        definitions.Add(def);

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var evt = new ComponentStatusChanged(
            ComponentId: componentId,
            SystemId: "SYS",
            PreviousStatus: ComponentStatus.Unknown,
            NewStatus: ComponentStatus.Normal,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await sut.UpdateComponentProgressAsync(evt);

        // Assert
        Assert.Contains(componentId, execution.CompletedComponents, StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task UpdateComponentProgressAsync_NoWatchingDefinition_DoesNothing()
    {
        // Arrange
        var (sut, _, executions, _, _, _, _, _, realtime, _) = BuildSut();
        // No definitions added

        var evt = new ComponentStatusChanged(
            ComponentId: "UNKNOWN",
            SystemId: "SYS",
            PreviousStatus: ComponentStatus.Running,
            NewStatus: ComponentStatus.Completed,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await sut.UpdateComponentProgressAsync(evt);

        // Assert
        Assert.IsEmpty(executions.Store);
        Assert.IsEmpty(realtime.NotifiedExecutionIds);
    }

    [TestMethod]
    public async Task UpdateComponentProgressAsync_ExecutionNotInProgress_SkipsProgress()
    {
        // Arrange
        var (sut, definitions, executions, _, _, _, _, _, realtime, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
        definitions.Add(def);

        // Execution is already terminal (Failed)
        var execution = new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: def.DefinitionId,
            systemId: "SYS",
            executionDate: DateOnly.FromDateTime(DateTime.Today),
            initialStatus: DailyExecutionStatus.Failed);
        executions.Add(execution);

        var evt = new ComponentStatusChanged(
            ComponentId: componentId,
            SystemId: "SYS",
            PreviousStatus: ComponentStatus.Running,
            NewStatus: ComponentStatus.Completed,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await sut.UpdateComponentProgressAsync(evt);

        // Assert — progress NOT recorded for terminal execution
        Assert.IsEmpty(execution.CompletedComponents);
        Assert.IsEmpty(realtime.NotifiedExecutionIds);
    }

    [TestMethod]
    public async Task UpdateComponentProgressAsync_ScheduledJobLost_DoesNotRecordProgress()
    {
        // Arrange
        var (sut, definitions, executions, _, _, _, _, _, realtime, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
        definitions.Add(def);

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var evt = new ComponentStatusChanged(
            ComponentId: componentId,
            SystemId: "SYS",
            PreviousStatus: ComponentStatus.Running,
            NewStatus: ComponentStatus.Lost, // Not Completed
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await sut.UpdateComponentProgressAsync(evt);

        // Assert
        Assert.IsEmpty(execution.CompletedComponents);
        Assert.IsEmpty(realtime.NotifiedExecutionIds);
    }

    // ── EvaluateDefinitionAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task EvaluateDefinitionAsync_AllJobsCompleted_FinalizesAsSuccess()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, email, _, _, broadcaster) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)],
            sendOnFailure: false); // don't require failure-only sends to test
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        execution.AddCompletedComponent(componentId); // mark as completed before deadline
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Completed);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.IsNotEmpty(inbox.Store); // FR-020: inbox always written
        var inboxItem = inbox.Store[0];
        Assert.AreEqual(NotificationType.HealthSuccess, inboxItem.NotificationType);
        Assert.IsNotEmpty(broadcaster.HealthPublished);
        Assert.AreEqual(NotificationType.HealthSuccess, broadcaster.HealthPublished[0].Type);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_JobNotCompleted_FinalizesAsFailed()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, email, _, _, broadcaster) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)],
            sendOnFailure: false);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        // NOT adding component to CompletedComponents
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Running);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.IsNotEmpty(inbox.Store); // FR-020
        Assert.AreEqual(NotificationType.HealthFailure, inbox.Store[0].NotificationType);
        Assert.AreEqual(NotificationType.HealthFailure, broadcaster.HealthPublished[0].Type);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_MaintenanceActive_FinalizesAsExempted()
    {
        // Arrange
        var (sut, definitions, executions, systems, _, inbox, _, _, _, broadcaster) = BuildSut();
        var def = MakeDefinition();
        definitions.Add(def);

        // System in maintenance
        systems.Add(MakeSystem(def.SystemId, maintenanceActive: true));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert — BI-006: maintenance → Exempted
        Assert.IsNotEmpty(inbox.Store);
        Assert.AreEqual(NotificationType.HealthExempted, inbox.Store[0].NotificationType);
        Assert.AreEqual(NotificationType.HealthExempted, broadcaster.HealthPublished[0].Type);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_BI013_TerminalExecution_SkipsEvaluation()
    {
        // Arrange
        var (sut, definitions, executions, systems, _, inbox, _, _, _, _) = BuildSut();
        var def = MakeDefinition();
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        // Execution already in terminal state
        var execution = new DailyExecution(
            executionId: Guid.NewGuid(),
            definitionId: def.DefinitionId,
            systemId: "SYS",
            executionDate: DateOnly.FromDateTime(DateTime.Today),
            initialStatus: DailyExecutionStatus.Success);
        executions.Add(execution);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert — BI-013: no inbox item created (already terminal)
        Assert.IsEmpty(inbox.Store);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_NoExecutionForToday_DoesNothing()
    {
        // Arrange
        var (sut, definitions, _, systems, _, inbox, _, _, _, _) = BuildSut();
        var def = MakeDefinition();
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));
        // No execution added

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.IsEmpty(inbox.Store);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_ServiceNormalAllSubIndicatorsNormal_Success()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, _, _, _, broadcaster) = BuildSut();
        const string componentId = "SVC-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.Service)],
            sendOnFailure: false);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        execution.AddCompletedComponent(componentId);
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Normal);
        state.SetSubIndicators([
            new SubIndicator("SUB-A", SubIndicatorStatus.Normal),
            new SubIndicator("SUB-B", SubIndicatorStatus.Normal)
        ]);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.AreEqual(NotificationType.HealthSuccess, broadcaster.HealthPublished[0].Type);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_ServiceSubIndicatorDegraded_Failed()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, _, _, _, broadcaster) = BuildSut();
        const string componentId = "SVC-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.Service)],
            sendOnFailure: false);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Normal);
        state.SetSubIndicators([
            new SubIndicator("SUB-A", SubIndicatorStatus.Normal),
            new SubIndicator("SUB-B", SubIndicatorStatus.Error) // not all Normal
        ]);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.AreEqual(NotificationType.HealthFailure, broadcaster.HealthPublished[0].Type);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_SendOnFailure_True_FailedExecution_SendsEmail()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, _, email, _, _, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)],
            sendOnFailure: true,
            emailRecipients: [new EmailAddress("ops@example.com")]);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        // Component not in completed list → will be Failed
        var state = new ComponentState(componentId, ComponentStatus.Running);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.AreEqual(1, email.SendHealthSummaryCallCount);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_SendOnFailure_True_SuccessExecution_SendsEmail()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, _, email, _, _, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)],
            sendOnFailure: true,
            emailRecipients: [new EmailAddress("ops@example.com")]);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        execution.AddCompletedComponent(componentId);
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Completed);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert — success notification always sent
        Assert.AreEqual(1, email.SendHealthSummaryCallCount);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_FR020_InboxAlwaysWritten_EvenWithNoEmailConfig()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, email, _, _, _) = BuildSut();
        const string componentId = "COMP-001";
        // No email recipients, no Teams URL
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Running);
        componentStates.Add(state);

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert — FR-020: inbox item written even with no notification config
        Assert.AreEqual(1, inbox.Store.Count);
        Assert.AreEqual(0, email.SendHealthSummaryCallCount);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_EmailDeliveryFails_WritesDeliveryFailedInboxItem()
    {
        // Arrange
        var (sut, definitions, executions, systems, componentStates, inbox, email, _, _, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)],
            sendOnFailure: true,
            emailRecipients: [new EmailAddress("ops@example.com")]);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);

        var state = new ComponentState(componentId, ComponentStatus.Running);
        componentStates.Add(state);

        // Simulate email failure
        email.ThrowOnHealthSummary = new EmailDeliveryException("SMTP error");

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert — 2 inbox items: one for health result + one for delivery failure
        Assert.IsGreaterThanOrEqualTo(2, inbox.Store.Count);
        Assert.IsTrue(inbox.Store.Any(i => i.NotificationType == NotificationType.NotificationDeliveryFailed));
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_DefinitionNotFound_DoesNotThrow()
    {
        // Arrange
        var (sut, _, _, _, _, inbox, _, _, _, _) = BuildSut();
        var missingId = Guid.NewGuid();

        // Act — should not throw
        await sut.EvaluateDefinitionAsync(missingId);

        // Assert
        Assert.IsEmpty(inbox.Store);
    }

    [TestMethod]
    public async Task EvaluateDefinitionAsync_ComponentStateMissing_TreatedAsFailed()
    {
        // Arrange
        var (sut, definitions, executions, systems, _, inbox, _, _, _, _) = BuildSut();
        const string componentId = "COMP-001";
        var def = MakeDefinition(
            watchedComponents: [new WatchedComponent(componentId, ComponentType.ScheduledJob)]);
        definitions.Add(def);
        systems.Add(MakeSystem(def.SystemId));

        var execution = MakeInProgressExecution(def.DefinitionId);
        executions.Add(execution);
        // No component state added → state will be null → HasMetCompletionConditionAtDeadline returns false

        // Act
        await sut.EvaluateDefinitionAsync(def.DefinitionId);

        // Assert
        Assert.AreEqual(NotificationType.HealthFailure, inbox.Store[0].NotificationType);
    }
}
