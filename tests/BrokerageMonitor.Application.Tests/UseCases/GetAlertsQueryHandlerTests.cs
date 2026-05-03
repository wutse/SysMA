using BrokerageMonitor.Application.UseCases.Alerts;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.UseCases;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

internal sealed class GetAlerts_AlertRepo : IAlertRecordRepository
{
  private readonly List<AlertRecord> _unacknowledged = [];
  private readonly List<AlertRecord> _history = [];

  /// <summary>Captures args passed to the last GetHistoryAsync call.</summary>
  public (DateTimeOffset From, DateTimeOffset To)? LastHistoryArgs { get; private set; }

  public void AddUnacknowledged(AlertRecord alert) => _unacknowledged.Add(alert);
  public void AddHistory(AlertRecord alert) => _history.Add(alert);

  public Task<IReadOnlyList<AlertRecord>> GetUnacknowledgedAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<AlertRecord>>([.. _unacknowledged]);

  public Task<bool> HasUnacknowledgedAlertAsync(string systemId, CancellationToken ct = default)
      => Task.FromResult(_unacknowledged.Any(a => a.SystemId == systemId));

  public Task<IReadOnlySet<string>> GetSystemsWithUnacknowledgedAlertAsync(CancellationToken ct = default)
      => Task.FromResult<IReadOnlySet<string>>(
          new HashSet<string>(_unacknowledged.Select(a => a.SystemId)));

  public Task AddAsync(AlertRecord alert, CancellationToken ct = default)
      => Task.CompletedTask;

  public Task AcknowledgeBySystemAsync(string systemId, string operatorName, DateTimeOffset acknowledgedAt, CancellationToken ct = default)
      => Task.CompletedTask;

  public Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
      string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
  {
    LastHistoryArgs = (from, to);
    return Task.FromResult<IReadOnlyList<AlertRecord>>([.. _history]);
  }
}

/// <summary>TimeProvider that always returns a fixed timestamp.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
  private readonly DateTimeOffset _fixed;
  public FixedTimeProvider(DateTimeOffset fixedUtcNow) => _fixed = fixedUtcNow;
  public override DateTimeOffset GetUtcNow() => _fixed;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class GetAlertsQueryHandlerTests
{
  private static AlertRecord MakeAlert(string systemId = "SYS-01") =>
      new(Guid.NewGuid(), systemId, "COMP-01", ComponentStatus.Lost, DateTimeOffset.UtcNow);

  [TestMethod]
  public async Task HandleAsync_UnacknowledgedOnly_ReturnsUnacknowledgedAlerts()
  {
    // Arrange
    var repo = new GetAlerts_AlertRepo();
    repo.AddUnacknowledged(MakeAlert());
    var sut = new GetAlertsQueryHandler(repo, TimeProvider.System);

    // Act
    var result = await sut.HandleAsync(new GetAlertsQuery(UnacknowledgedOnly: true));

    // Assert
    Assert.HasCount(1, result);
  }

  [TestMethod]
  public async Task HandleAsync_UnacknowledgedOnly_DoesNotReturnHistory()
  {
    // Arrange — only history records exist, not unacknowledged
    var repo = new GetAlerts_AlertRepo();
    repo.AddHistory(MakeAlert());
    var sut = new GetAlertsQueryHandler(repo, TimeProvider.System);

    // Act
    var result = await sut.HandleAsync(new GetAlertsQuery(UnacknowledgedOnly: true));

    // Assert — GetUnacknowledgedAsync was called, history list is empty
    Assert.IsEmpty(result);
  }

  [TestMethod]
  public async Task HandleAsync_HistoryMode_ReturnsHistoryAlerts()
  {
    // Arrange
    var repo = new GetAlerts_AlertRepo();
    repo.AddHistory(MakeAlert("SYS-02"));
    var sut = new GetAlertsQueryHandler(repo, TimeProvider.System);
    var from = DateTimeOffset.UtcNow.AddDays(-7);
    var to = DateTimeOffset.UtcNow;

    // Act
    var result = await sut.HandleAsync(new GetAlertsQuery(UnacknowledgedOnly: false, From: from, To: to));

    // Assert
    Assert.HasCount(1, result);
    Assert.AreEqual("SYS-02", result[0].SystemId);
  }

  [TestMethod]
  public async Task HandleAsync_NullQuery_ThrowsArgumentNullException()
  {
    // Arrange
    var sut = new GetAlertsQueryHandler(new GetAlerts_AlertRepo(), TimeProvider.System);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(() =>
        sut.HandleAsync(null!));
  }

  /// <summary>
  /// N4: When both From and To are null, the handler must derive both values from a
  /// single clock read so that from + 7 days == to exactly (single-tick consistency).
  /// </summary>
  [TestMethod]
  public async Task HandleAsync_NullFromAndTo_FromAndToAreConsistentSingleClockRead()
  {
    // Arrange — freeze time so the single GetUtcNow() call returns a known value
    var frozenNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    var repo = new GetAlerts_AlertRepo();
    var sut = new GetAlertsQueryHandler(repo, new FixedTimeProvider(frozenNow));

    // Act
    await sut.HandleAsync(new GetAlertsQuery(UnacknowledgedOnly: false, From: null, To: null));

    // Assert — from and to were both derived from the same frozen "now"
    Assert.IsNotNull(repo.LastHistoryArgs);
    Assert.AreEqual(frozenNow.AddDays(-7), repo.LastHistoryArgs!.Value.From,
        "From should be exactly now - 7 days from a single clock read");
    Assert.AreEqual(frozenNow, repo.LastHistoryArgs.Value.To,
        "To should be exactly now from the same clock read");
  }
}
