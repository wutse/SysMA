using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Notifications;

namespace BrokerageMonitor.Infrastructure.Tests.Notifications;

// ---------------------------------------------------------------------------
// Test double: captures every AddAsync call
// ---------------------------------------------------------------------------

internal sealed class SpyAuditLogRepository : IAuditLogRepository
{
    private readonly List<AuditLogEntry> _entries = [];
    public IReadOnlyList<AuditLogEntry> Entries => _entries;

    public Task AddAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AuditLogEntry>>([]);

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// AuditLogger unit tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class AuditLoggerTests
{
    private readonly SpyAuditLogRepository _spy;
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _spy = new SpyAuditLogRepository();
        _sut = new AuditLogger(_spy);
    }

    // ---- LogStatusChangedAsync ----

    [TestMethod]
    public async Task LogStatusChangedAsync_ValidArgs_AddsOneEntry()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogStatusChangedAsync("SYS-01", "COMP-01",
            ComponentStatus.Normal, ComponentStatus.Lost, occurredAt);

        // Assert
        Assert.HasCount(1, _spy.Entries);
    }

    [TestMethod]
    public async Task LogStatusChangedAsync_ValidArgs_SetsOperatorNameToSystem()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogStatusChangedAsync("SYS-01", "COMP-01",
            ComponentStatus.Normal, ComponentStatus.Lost, occurredAt);

        // Assert
        Assert.AreEqual("System", _spy.Entries[0].OperatorName);
    }

    [TestMethod]
    public async Task LogStatusChangedAsync_ValidArgs_EncodesStatusTransitionInActionType()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogStatusChangedAsync("SYS-01", "COMP-01",
            ComponentStatus.Normal, ComponentStatus.Lost, occurredAt);

        // Assert
        StringAssert.Contains(_spy.Entries[0].ActionType, "Normal");
        StringAssert.Contains(_spy.Entries[0].ActionType, "Lost");
    }

    [TestMethod]
    public async Task LogStatusChangedAsync_ValidArgs_PreservesSystemAndComponentId()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogStatusChangedAsync("SYS-01", "COMP-01",
            ComponentStatus.Normal, ComponentStatus.Lost, occurredAt);

        // Assert
        var entry = _spy.Entries[0];
        Assert.AreEqual("SYS-01",  entry.SystemId);
        Assert.AreEqual("COMP-01", entry.ComponentId);
        Assert.AreEqual(occurredAt, entry.OccurredAt);
    }

    // ---- LogOperatorActionAsync ----

    [TestMethod]
    public async Task LogOperatorActionAsync_ValidArgs_AddsOneEntry()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogOperatorActionAsync("SYS-01", null,
            "ToggleMaintenance", "operator1", "scheduled window", occurredAt);

        // Assert
        Assert.HasCount(1, _spy.Entries);
    }

    [TestMethod]
    public async Task LogOperatorActionAsync_ValidArgs_PreservesAllFields()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogOperatorActionAsync("SYS-01", "COMP-02",
            "AcknowledgeAlert", "alice", "false alarm", occurredAt);

        // Assert
        var entry = _spy.Entries[0];
        Assert.AreEqual("SYS-01",         entry.SystemId);
        Assert.AreEqual("COMP-02",        entry.ComponentId);
        Assert.AreEqual("AcknowledgeAlert", entry.ActionType);
        Assert.AreEqual("alice",          entry.OperatorName);
        Assert.AreEqual("false alarm",    entry.Reason);
        Assert.AreEqual(occurredAt,       entry.OccurredAt);
    }

    [TestMethod]
    public async Task LogOperatorActionAsync_NullComponentId_StoresNullComponentId()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogOperatorActionAsync("SYS-01", null,
            "ToggleMaintenance", "operator1", null, occurredAt);

        // Assert
        Assert.IsNull(_spy.Entries[0].ComponentId);
    }

    [TestMethod]
    public async Task LogOperatorActionAsync_EachCall_GeneratesUniqueAuditId()
    {
        // Arrange
        var occurredAt = DateTimeOffset.UtcNow;

        // Act
        await _sut.LogOperatorActionAsync("SYS-01", null, "ActionA", "op", null, occurredAt);
        await _sut.LogOperatorActionAsync("SYS-01", null, "ActionB", "op", null, occurredAt);

        // Assert
        Assert.AreNotEqual(_spy.Entries[0].AuditId, _spy.Entries[1].AuditId);
    }
}
