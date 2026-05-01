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

  public Task AcknowledgeBySystemAsync(string systemId, string operatorName, CancellationToken ct = default)
      => Task.CompletedTask;

  public Task<IReadOnlyList<AlertRecord>> GetHistoryAsync(
      string? systemId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<AlertRecord>>([.. _history]);
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
    var sut = new GetAlertsQueryHandler(repo);

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
    var sut = new GetAlertsQueryHandler(repo);

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
    var sut = new GetAlertsQueryHandler(repo);
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
    var sut = new GetAlertsQueryHandler(new GetAlerts_AlertRepo());

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(() =>
        sut.HandleAsync(null!));
  }
}
