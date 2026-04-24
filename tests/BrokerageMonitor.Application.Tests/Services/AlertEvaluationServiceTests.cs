using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

// ---------------------------------------------------------------------------
// Test doubles (prefixed AlertEval_ to avoid cross-file naming conflicts)
// ---------------------------------------------------------------------------

internal sealed class AlertEval_SystemRepo : IMonitoredSystemRepository
{
    private readonly Dictionary<string, MonitoredSystem> _store = new();

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

internal sealed class AlertEval_AlertRepo : IAlertRecordRepository
{
    private readonly List<AlertRecord> _store = new();
    private bool _hasUnacknowledged;

    public IReadOnlyList<AlertRecord> Store => _store.AsReadOnly();

    public void SetHasUnacknowledged(bool value) => _hasUnacknowledged = value;

    public Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>(_store.Where(a => !a.IsAcknowledged).ToList());

    public Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult(_hasUnacknowledged);

    public Task<IReadOnlySet<string>> GetSystemsWithUnacknowledgedAlertAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(
            _store.Where(a => !a.IsAcknowledged)
                  .Select(a => a.SystemId)
                  .ToHashSet());

    public Task AddAsync(AlertRecord alert, CancellationToken ct = default)
    {
        _store.Add(alert);
        return Task.CompletedTask;
    }

    public Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AlertRecord>>([]);
}

internal sealed class AlertEval_EmailService : IEmailNotificationService
{
    public List<AlertEmailRequest> SentAlerts { get; } = new();
    public bool ShouldThrow { get; set; }

    public Task SendAlertAsync(AlertEmailRequest request, CancellationToken ct = default)
    {
        if (ShouldThrow)
            throw new EmailDeliveryException("SMTP failure (test)");

        SentAlerts.Add(request);
        return Task.CompletedTask;
    }

    public Task SendHealthSummaryAsync(HealthSummaryEmailRequest request, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AlertEval_RealtimeService : IRealtimeNotificationService
{
    public List<(string SystemId, string ComponentId)> AlertTriggeredCalls { get; } = new();

    public Task NotifyComponentStatusChangedAsync(ComponentStatusChanged evt, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyAlertTriggeredAsync(string systemId, string componentId, CancellationToken ct = default)
    {
        AlertTriggeredCalls.Add((systemId, componentId));
        return Task.CompletedTask;
    }

    public Task NotifyAlertAcknowledgedAsync(string systemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyMaintenanceModeChangedAsync(string systemId, bool isActive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task NotifyDailyExecutionUpdatedAsync(Guid executionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AlertEval_InboxRepo : INotificationInboxRepository
{
    public List<NotificationInboxItem> Items { get; } = new();

    public Task AddAsync(NotificationInboxItem item, CancellationToken ct = default)
    {
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationInboxItem>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NotificationInboxItem>>([.. Items]);

    public Task MarkAsReadAsync(Guid itemId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class AlertEval_ComponentRepo : IMonitoredComponentRepository
{
    public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default)
        => Task.FromResult<MonitoredComponent?>(null);

    public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(string systemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([]);

    public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MonitoredComponent>>([]);

    public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Builder helpers
// ---------------------------------------------------------------------------

internal static class AlertEval_Builder
{
    private static readonly MarketSessionWindow DefaultSession =
        new(new TimeOnly(9, 0), new TimeOnly(17, 0));

    public static MonitoredSystem BuildActive(string id = "SYS-1") =>
        new(id, "Test System", DefaultSession, isActive: true);

    public static MonitoredSystem BuildWithMaintenanceActive(string id = "SYS-1")
    {
        var sys = new MonitoredSystem(id, "Test System", DefaultSession, isActive: true);
        sys.ActivateMaintenance("operator");
        return sys;
    }

    /// <summary>Session 01:00–02:00 UTC — noon UTC is never inside it.</summary>
    public static MonitoredSystem BuildWithNightSession(string id = "SYS-1") =>
        new(id, "Test System",
            new MarketSessionWindow(new TimeOnly(1, 0), new TimeOnly(2, 0)),
            isActive: true);

    /// <summary>Event OccurredAt pinned to 12:00 UTC — always inside 09:00–17:00 session.</summary>
    public static ComponentStatusChanged EvtInsideSession(
        ComponentStatus status,
        string systemId = "SYS-1",
        string componentId = "COMP-1")
    {
        var today = DateTimeOffset.UtcNow.Date;
        var noon = new DateTimeOffset(today.Year, today.Month, today.Day, 12, 0, 0, TimeSpan.Zero);
        return new(componentId, systemId, ComponentStatus.Normal, status, noon);
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AlertEvaluationServiceTests
{
    private readonly AlertEval_SystemRepo _systemRepo = new();
    private readonly AlertEval_AlertRepo _alertRepo = new();
    private readonly AlertEval_EmailService _emailService = new();
    private readonly AlertEval_RealtimeService _realtimeService = new();
    private readonly AlertEval_InboxRepo _inboxRepo = new();
    private readonly AlertEvaluationService _sut;

    public AlertEvaluationServiceTests()
    {
        _sut = new AlertEvaluationService(
            _systemRepo,
            new AlertEval_ComponentRepo(),
            _alertRepo,
            _emailService,
            _realtimeService,
            _inboxRepo,
            NullLogger<AlertEvaluationService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Non-alertable statuses
    // -------------------------------------------------------------------------

    [TestMethod]
    [DataRow(ComponentStatus.Normal)]
    [DataRow(ComponentStatus.Running)]
    [DataRow(ComponentStatus.Idle)]
    [DataRow(ComponentStatus.Stopped)]
    [DataRow(ComponentStatus.Completed)]
    [DataRow(ComponentStatus.Failed)]
    [DataRow(ComponentStatus.Maintenance)]
    [DataRow(ComponentStatus.Unknown)]
    public async Task EvaluateAsync_NonAlertableStatus_NoAlertCreated(ComponentStatus status)
    {
        // Arrange
        _systemRepo.Add(AlertEval_Builder.BuildActive());

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(status));

        // Assert
        Assert.AreEqual(0, _alertRepo.Store.Count);
        Assert.AreEqual(0, _realtimeService.AlertTriggeredCalls.Count);
    }

    // -------------------------------------------------------------------------
    // Maintenance mode suppression (BI-006)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_MaintenanceModeActive_AlertSuppressed()
    {
        // Arrange
        _systemRepo.Add(AlertEval_Builder.BuildWithMaintenanceActive());

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(0, _alertRepo.Store.Count, "Alert must not be created in maintenance mode (BI-006).");
    }

    // -------------------------------------------------------------------------
    // Out-of-session suppression (FR-030)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_OutsideMarketSession_AlertSuppressed()
    {
        // Arrange — session 01:00–02:00 UTC; event at noon UTC (outside session)
        _systemRepo.Add(AlertEval_Builder.BuildWithNightSession());

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Lost));

        // Assert
        Assert.AreEqual(0, _alertRepo.Store.Count, "Alert must not be created outside market session (FR-030).");
    }

    // -------------------------------------------------------------------------
    // Duplicate alert suppression (BI-007)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_UnacknowledgedAlertExists_AlertSuppressed()
    {
        // Arrange
        _systemRepo.Add(AlertEval_Builder.BuildActive());
        _alertRepo.SetHasUnacknowledged(true);

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Warning));

        // Assert
        Assert.AreEqual(0, _alertRepo.Store.Count,
            "Duplicate alert must be suppressed while GlobalFlag is active (BI-007).");
    }

    // -------------------------------------------------------------------------
    // Successful alert trigger (FR-010)
    // -------------------------------------------------------------------------

    [TestMethod]
    [DataRow(ComponentStatus.Lost)]
    [DataRow(ComponentStatus.Error)]
    [DataRow(ComponentStatus.Warning)]
    public async Task EvaluateAsync_AlertableStatusInsideSession_AlertRecordCreated(ComponentStatus status)
    {
        // Arrange
        var system = new MonitoredSystem(
            "SYS-1", "My System",
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            alertRecipients: [new EmailAddress("ops@example.com")],
            isActive: true);
        _systemRepo.Add(system);

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(status));

        // Assert
        Assert.AreEqual(1, _alertRepo.Store.Count);
        var alert = _alertRepo.Store[0];
        Assert.AreEqual("SYS-1", alert.SystemId);
        Assert.AreEqual("COMP-1", alert.ComponentId);
        Assert.AreEqual(status, alert.AlertStatus);
        Assert.IsTrue(alert.IsGlobalFlagActive);
    }

    [TestMethod]
    public async Task EvaluateAsync_AlertTriggered_SignalRPushCalled()
    {
        // Arrange
        var system = new MonitoredSystem(
            "SYS-1", "My System",
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            alertRecipients: [new EmailAddress("ops@example.com")],
            isActive: true);
        _systemRepo.Add(system);

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(1, _realtimeService.AlertTriggeredCalls.Count);
        Assert.AreEqual("SYS-1", _realtimeService.AlertTriggeredCalls[0].SystemId);
        Assert.AreEqual("COMP-1", _realtimeService.AlertTriggeredCalls[0].ComponentId);
    }

    [TestMethod]
    public async Task EvaluateAsync_AlertTriggeredWithRecipients_EmailSent()
    {
        // Arrange
        var system = new MonitoredSystem(
            "SYS-1", "My System",
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            alertRecipients: [new EmailAddress("ops@example.com")],
            isActive: true);
        _systemRepo.Add(system);

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Lost));

        // Assert
        Assert.AreEqual(1, _emailService.SentAlerts.Count);
        Assert.AreEqual("ops@example.com", _emailService.SentAlerts[0].Recipients[0]);
        Assert.AreEqual(ComponentStatus.Lost, _emailService.SentAlerts[0].AlertStatus);
    }

    [TestMethod]
    public async Task EvaluateAsync_NoRecipients_NoEmailSentButAlertCreated()
    {
        // Arrange — system has no alert recipients
        _systemRepo.Add(AlertEval_Builder.BuildActive());

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(1, _alertRepo.Store.Count, "Alert should still be persisted.");
        Assert.AreEqual(0, _emailService.SentAlerts.Count, "No email should be sent without recipients.");
    }

    // -------------------------------------------------------------------------
    // Email failure → inbox item (US-031 acceptance criteria)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_EmailDeliveryFails_WritesNotificationDeliveryFailedInboxItem()
    {
        // Arrange
        var system = new MonitoredSystem(
            "SYS-1", "My System",
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            alertRecipients: [new EmailAddress("ops@example.com")],
            isActive: true);
        _systemRepo.Add(system);
        _emailService.ShouldThrow = true;

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(1, _alertRepo.Store.Count, "AlertRecord must be persisted even when email fails.");
        Assert.AreEqual(1, _inboxRepo.Items.Count, "NotificationDeliveryFailed item must be written.");
        Assert.AreEqual(NotificationType.NotificationDeliveryFailed, _inboxRepo.Items[0].NotificationType);
        Assert.AreEqual(AlertEvaluationService.AlertEmailFailureDefinitionId, _inboxRepo.Items[0].DefinitionId);
    }

    [TestMethod]
    public async Task EvaluateAsync_EmailDeliveryFails_SignalRPushStillCalled()
    {
        // Arrange
        var system = new MonitoredSystem(
            "SYS-1", "My System",
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            alertRecipients: [new EmailAddress("ops@example.com")],
            isActive: true);
        _systemRepo.Add(system);
        _emailService.ShouldThrow = true;

        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(1, _realtimeService.AlertTriggeredCalls.Count,
            "SignalR push must still be sent even when email fails.");
    }

    // -------------------------------------------------------------------------
    // Unknown system
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_UnknownSystem_NoAlertCreated()
    {
        // Arrange — no system registered
        // Act
        await _sut.EvaluateAsync(AlertEval_Builder.EvtInsideSession(ComponentStatus.Error));

        // Assert
        Assert.AreEqual(0, _alertRepo.Store.Count);
    }

    // -------------------------------------------------------------------------
    // Null guard
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task EvaluateAsync_NullEvent_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.EvaluateAsync(null!));
    }
}
