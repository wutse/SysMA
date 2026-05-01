using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

/// <summary>
/// Verifies construction, property mapping, and record equality for all domain event types
/// that had 0 % coverage per the test-quality-report.
/// </summary>
[TestClass]
public sealed class DomainEventTests
{
  // -----------------------------------------------------------------------
  // AggregateHealthDeadlineReached
  // -----------------------------------------------------------------------

  [TestMethod]
  public void AggregateHealthDeadlineReached_Constructor_MapsAllProperties()
  {
    // Arrange
    var id = Guid.NewGuid();
    var date = DateOnly.FromDateTime(DateTime.Today);
    var occurred = DateTimeOffset.UtcNow;

    // Act
    var evt = new AggregateHealthDeadlineReached(id, "SYS-01", date, occurred);

    // Assert
    Assert.AreEqual(id, evt.DefinitionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(date, evt.ExecutionDate);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  [TestMethod]
  public void AggregateHealthDeadlineReached_RecordEquality_EqualWhenSameValues()
  {
    var id = Guid.NewGuid();
    var date = DateOnly.FromDateTime(DateTime.Today);
    var occurred = DateTimeOffset.UtcNow;

    var a = new AggregateHealthDeadlineReached(id, "SYS-01", date, occurred);
    var b = new AggregateHealthDeadlineReached(id, "SYS-01", date, occurred);

    Assert.AreEqual(a, b);
    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }

  // -----------------------------------------------------------------------
  // AlertAcknowledged
  // -----------------------------------------------------------------------

  [TestMethod]
  public void AlertAcknowledged_Constructor_MapsAllProperties()
  {
    var alertId = Guid.NewGuid();
    var occurred = DateTimeOffset.UtcNow;

    var evt = new AlertAcknowledged(alertId, "SYS-01", "operator1", occurred);

    Assert.AreEqual(alertId, evt.AlertId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual("operator1", evt.OperatorName);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  [TestMethod]
  public void AlertAcknowledged_RecordEquality_EqualWhenSameValues()
  {
    var alertId = Guid.NewGuid();
    var occurred = DateTimeOffset.UtcNow;

    var a = new AlertAcknowledged(alertId, "SYS-01", "op", occurred);
    var b = new AlertAcknowledged(alertId, "SYS-01", "op", occurred);

    Assert.AreEqual(a, b);
  }

  // -----------------------------------------------------------------------
  // DailyExecutionCreated
  // -----------------------------------------------------------------------

  [TestMethod]
  public void DailyExecutionCreated_Constructor_MapsAllProperties()
  {
    var execId = Guid.NewGuid();
    var defId = Guid.NewGuid();
    var date = DateOnly.FromDateTime(DateTime.Today);
    var occurred = DateTimeOffset.UtcNow;

    var evt = new DailyExecutionCreated(execId, defId, "SYS-01", date, occurred);

    Assert.AreEqual(execId, evt.ExecutionId);
    Assert.AreEqual(defId, evt.DefinitionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(date, evt.ExecutionDate);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  // -----------------------------------------------------------------------
  // DailyExecutionCompleted
  // -----------------------------------------------------------------------

  [TestMethod]
  [DataRow(DailyExecutionStatus.Success)]
  [DataRow(DailyExecutionStatus.Failed)]
  public void DailyExecutionCompleted_Constructor_MapsStatus(DailyExecutionStatus status)
  {
    var execId = Guid.NewGuid();
    var defId = Guid.NewGuid();
    var occurred = DateTimeOffset.UtcNow;

    var evt = new DailyExecutionCompleted(execId, defId, "SYS-01", status, occurred);

    Assert.AreEqual(execId, evt.ExecutionId);
    Assert.AreEqual(defId, evt.DefinitionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(status, evt.FinalStatus);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  // -----------------------------------------------------------------------
  // DailyExecutionExempted
  // -----------------------------------------------------------------------

  [TestMethod]
  public void DailyExecutionExempted_Constructor_MapsAllProperties()
  {
    var execId = Guid.NewGuid();
    var defId = Guid.NewGuid();
    var occurred = DateTimeOffset.UtcNow;

    var evt = new DailyExecutionExempted(execId, defId, "SYS-01", occurred);

    Assert.AreEqual(execId, evt.ExecutionId);
    Assert.AreEqual(defId, evt.DefinitionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  // -----------------------------------------------------------------------
  // DailyExecutionMissed
  // -----------------------------------------------------------------------

  [TestMethod]
  public void DailyExecutionMissed_Constructor_MapsAllProperties()
  {
    var execId = Guid.NewGuid();
    var defId = Guid.NewGuid();
    var date = DateOnly.FromDateTime(DateTime.Today);
    var occurred = DateTimeOffset.UtcNow;

    var evt = new DailyExecutionMissed(execId, defId, "SYS-01", date, "station restart", occurred);

    Assert.AreEqual(execId, evt.ExecutionId);
    Assert.AreEqual(defId, evt.DefinitionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(date, evt.ExecutionDate);
    Assert.AreEqual("station restart", evt.MissedReason);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  // -----------------------------------------------------------------------
  // HealthNotificationSent
  // -----------------------------------------------------------------------

  [TestMethod]
  [DataRow(NotificationType.HealthSuccess)]
  [DataRow(NotificationType.HealthFailure)]
  public void HealthNotificationSent_Constructor_MapsNotificationType(NotificationType type)
  {
    var defId = Guid.NewGuid();
    var execId = Guid.NewGuid();
    var occurred = DateTimeOffset.UtcNow;

    var evt = new HealthNotificationSent(defId, execId, "SYS-01", type, occurred);

    Assert.AreEqual(defId, evt.DefinitionId);
    Assert.AreEqual(execId, evt.ExecutionId);
    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(type, evt.NotificationType);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  // -----------------------------------------------------------------------
  // MaintenanceModeToggled
  // -----------------------------------------------------------------------

  [TestMethod]
  [DataRow(true)]
  [DataRow(false)]
  public void MaintenanceModeToggled_Constructor_MapsIsActive(bool isActive)
  {
    var occurred = DateTimeOffset.UtcNow;

    var evt = new MaintenanceModeToggled("SYS-01", isActive, "operator1", occurred);

    Assert.AreEqual("SYS-01", evt.SystemId);
    Assert.AreEqual(isActive, evt.IsActive);
    Assert.AreEqual("operator1", evt.OperatorName);
    Assert.AreEqual(occurred, evt.OccurredAt);
  }

  [TestMethod]
  public void MaintenanceModeToggled_RecordEquality_EqualWhenSameValues()
  {
    var occurred = DateTimeOffset.UtcNow;

    var a = new MaintenanceModeToggled("SYS-01", true, "op", occurred);
    var b = new MaintenanceModeToggled("SYS-01", true, "op", occurred);

    Assert.AreEqual(a, b);
    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }

  // -----------------------------------------------------------------------
  // IDomainEvent implementation check
  // -----------------------------------------------------------------------

  [TestMethod]
  public void AllZeroCoverageEvents_ImplementIDomainEvent()
  {
    Assert.IsInstanceOfType<IDomainEvent>(
        new AggregateHealthDeadlineReached(Guid.NewGuid(), "S", DateOnly.MinValue, DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new AlertAcknowledged(Guid.NewGuid(), "S", "op", DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new DailyExecutionCreated(Guid.NewGuid(), Guid.NewGuid(), "S", DateOnly.MinValue, DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new DailyExecutionCompleted(Guid.NewGuid(), Guid.NewGuid(), "S", DailyExecutionStatus.Success, DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new DailyExecutionExempted(Guid.NewGuid(), Guid.NewGuid(), "S", DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new DailyExecutionMissed(Guid.NewGuid(), Guid.NewGuid(), "S", DateOnly.MinValue, "r", DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new HealthNotificationSent(Guid.NewGuid(), Guid.NewGuid(), "S", NotificationType.HealthSuccess, DateTimeOffset.UtcNow));
    Assert.IsInstanceOfType<IDomainEvent>(
        new MaintenanceModeToggled("S", false, "op", DateTimeOffset.UtcNow));
  }
}
