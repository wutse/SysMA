using BrokerageMonitor.Application.UseCases.History;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class History_ExecutionHistoryRepo : IExecutionHistoryRepository
{
    private readonly List<ExecutionHistoryEntry> _data = [];

    public void Add(ExecutionHistoryEntry entry) => _data.Add(entry);

    public Task AddAsync(ExecutionHistoryEntry entry, CancellationToken ct = default)
    {
        _data.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionHistoryEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = _data
            .Where(e => (systemId is null || e.SystemId == systemId)
                        && e.StartedAt >= from && e.StartedAt <= to)
            .ToList();

        return Task.FromResult<IReadOnlyList<ExecutionHistoryEntry>>(results);
    }

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class History_AuditLogRepo : IAuditLogRepository
{
    private readonly List<AuditLogEntry> _data = [];

    public void Add(AuditLogEntry entry) => _data.Add(entry);

    public Task AddAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        _data.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = _data
            .Where(e => (systemId is null || e.SystemId == systemId)
                        && e.OccurredAt >= from && e.OccurredAt <= to)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditLogEntry>>(results);
    }

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// GetExecutionHistoryQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetExecutionHistoryQueryHandlerTests
{
    private static readonly DateTimeOffset _base = new(2026, 4, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly History_ExecutionHistoryRepo _repo = new();
    private readonly GetExecutionHistoryQueryHandler _sut;

    public GetExecutionHistoryQueryHandlerTests()
    {
        _sut = new GetExecutionHistoryQueryHandler(_repo);
    }

    private static ExecutionHistoryEntry MakeEntry(string systemId, DateTimeOffset startedAt)
        => new(Guid.NewGuid(), systemId, "COMP-1", "Worker", ComponentType.Service,
               ComponentStatus.Normal, startedAt, startedAt.AddSeconds(5), null);

    // ── Null guard ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.HandleAsync(null!));
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullSystemId_ReturnsAllSystems()
    {
        // Arrange
        _repo.Add(MakeEntry("SYS-A", _base));
        _repo.Add(MakeEntry("SYS-B", _base));

        // Act
        var result = await _sut.HandleAsync(new GetExecutionHistoryQuery(null, _base.AddHours(-1), _base.AddHours(1)));

        // Assert
        Assert.HasCount(2, result);
    }

    [TestMethod]
    public async Task HandleAsync_SystemIdFilter_ReturnsOnlyMatchingSystem()
    {
        // Arrange
        _repo.Add(MakeEntry("SYS-A", _base));
        _repo.Add(MakeEntry("SYS-B", _base));

        // Act
        var result = await _sut.HandleAsync(new GetExecutionHistoryQuery("SYS-A", _base.AddHours(-1), _base.AddHours(1)));

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("SYS-A", result[0].SystemId);
    }

    [TestMethod]
    public async Task HandleAsync_DateRangeExcludes_ReturnsEmpty()
    {
        // Arrange
        _repo.Add(MakeEntry("SYS-A", _base));

        // Act — query window is entirely before the entry
        var result = await _sut.HandleAsync(
            new GetExecutionHistoryQuery(null, _base.AddHours(-5), _base.AddHours(-1)));

        // Assert
        Assert.IsEmpty(result);
    }
}

// ---------------------------------------------------------------------------
// GetAuditLogsQueryHandlerTests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetAuditLogsQueryHandlerTests
{
    private static readonly DateTimeOffset _base = new(2026, 4, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly History_AuditLogRepo _repo = new();
    private readonly GetAuditLogsQueryHandler _sut;

    public GetAuditLogsQueryHandlerTests()
    {
        _sut = new GetAuditLogsQueryHandler(_repo);
    }

    private static AuditLogEntry MakeLog(string systemId, DateTimeOffset at)
        => new(Guid.NewGuid(), systemId, null, "StateChanged", "Operator", null, at);

    // ── Null guard ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.HandleAsync(null!));
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task HandleAsync_NullSystemId_ReturnsAllSystems()
    {
        // Arrange
        _repo.Add(MakeLog("SYS-A", _base));
        _repo.Add(MakeLog("SYS-B", _base));

        // Act
        var result = await _sut.HandleAsync(new GetAuditLogsQuery(null, _base.AddHours(-1), _base.AddHours(1)));

        // Assert
        Assert.HasCount(2, result);
    }

    [TestMethod]
    public async Task HandleAsync_SystemIdFilter_ReturnsOnlyMatchingSystem()
    {
        // Arrange
        _repo.Add(MakeLog("SYS-A", _base));
        _repo.Add(MakeLog("SYS-B", _base));

        // Act
        var result = await _sut.HandleAsync(new GetAuditLogsQuery("SYS-B", _base.AddHours(-1), _base.AddHours(1)));

        // Assert
        Assert.HasCount(1, result);
        Assert.AreEqual("SYS-B", result[0].SystemId);
    }

    [TestMethod]
    public async Task HandleAsync_DateRangeExcludes_ReturnsEmpty()
    {
        // Arrange
        _repo.Add(MakeLog("SYS-A", _base));

        // Act — query window is after the entry
        var result = await _sut.HandleAsync(
            new GetAuditLogsQuery(null, _base.AddHours(1), _base.AddHours(5)));

        // Assert
        Assert.IsEmpty(result);
    }
}
