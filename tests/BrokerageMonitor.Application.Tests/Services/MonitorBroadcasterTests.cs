using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="MonitorBroadcaster"/> covering all four remaining
/// Publish* methods and their exception-in-subscriber branches (§5.2).
/// </summary>
[TestClass]
public sealed class MonitorBroadcasterTests
{
  private readonly MonitorBroadcaster _sut = new(NullLogger<MonitorBroadcaster>.Instance);

  // ── PublishComponentStatusChanged ────────────────────────────────────────

  [TestMethod]
  public void PublishComponentStatusChanged_WithSubscriber_InvokesHandler()
  {
    // Arrange
    string? capturedComponent = null;
    string? capturedSystem = null;
    ComponentStatus? capturedStatus = null;

    _sut.ComponentStatusUpdated += (comp, sys, status) =>
    {
      capturedComponent = comp;
      capturedSystem = sys;
      capturedStatus = status;
    };

    // Act
    _sut.PublishComponentStatusChanged("COMP-01", "SYS-01", ComponentStatus.Error);

    // Assert
    Assert.AreEqual("COMP-01", capturedComponent);
    Assert.AreEqual("SYS-01", capturedSystem);
    Assert.AreEqual(ComponentStatus.Error, capturedStatus);
  }

  [TestMethod]
  public void PublishComponentStatusChanged_NoSubscribers_DoesNotThrow()
  {
    _sut.PublishComponentStatusChanged("COMP-01", "SYS-01", ComponentStatus.Normal);
  }

  [TestMethod]
  public void PublishComponentStatusChanged_HandlerThrows_DoesNotPropagate()
  {
    // Arrange
    _sut.ComponentStatusUpdated += (_, _, _) =>
        throw new InvalidOperationException("subscriber failure");

    // Act / Assert — must swallow exception and not rethrow
    _sut.PublishComponentStatusChanged("COMP-01", "SYS-01", ComponentStatus.Lost);
  }

  [TestMethod]
  public void PublishComponentStatusChanged_MultipleSubscribers_AllInvoked()
  {
    // Arrange
    int callCount = 0;
    _sut.ComponentStatusUpdated += (_, _, _) => callCount++;
    _sut.ComponentStatusUpdated += (_, _, _) => callCount++;

    // Act
    _sut.PublishComponentStatusChanged("C", "S", ComponentStatus.Running);

    // Assert
    Assert.AreEqual(2, callCount);
  }

  // ── PublishAlertTriggered ─────────────────────────────────────────────────

  [TestMethod]
  public void PublishAlertTriggered_WithSubscriber_InvokesHandler()
  {
    // Arrange
    string? capturedSystem = null;
    string? capturedComponent = null;

    _sut.AlertTriggered += (sys, comp) =>
    {
      capturedSystem = sys;
      capturedComponent = comp;
    };

    // Act
    _sut.PublishAlertTriggered("SYS-01", "COMP-01");

    // Assert
    Assert.AreEqual("SYS-01", capturedSystem);
    Assert.AreEqual("COMP-01", capturedComponent);
  }

  [TestMethod]
  public void PublishAlertTriggered_NoSubscribers_DoesNotThrow()
  {
    _sut.PublishAlertTriggered("SYS-01", "COMP-01");
  }

  [TestMethod]
  public void PublishAlertTriggered_HandlerThrows_DoesNotPropagate()
  {
    _sut.AlertTriggered += (_, _) =>
        throw new InvalidOperationException("subscriber failure");

    _sut.PublishAlertTriggered("SYS-01", "COMP-01");
  }

  [TestMethod]
  public void PublishAlertTriggered_MultipleSubscribers_AllInvoked()
  {
    int callCount = 0;
    _sut.AlertTriggered += (_, _) => callCount++;
    _sut.AlertTriggered += (_, _) => callCount++;

    _sut.PublishAlertTriggered("S", "C");

    Assert.AreEqual(2, callCount);
  }

  // ── PublishAlertAcknowledged ──────────────────────────────────────────────

  [TestMethod]
  public void PublishAlertAcknowledged_WithSubscriber_InvokesHandler()
  {
    // Arrange
    string? capturedSystem = null;
    _sut.AlertAcknowledged += sys => capturedSystem = sys;

    // Act
    _sut.PublishAlertAcknowledged("SYS-01");

    // Assert
    Assert.AreEqual("SYS-01", capturedSystem);
  }

  [TestMethod]
  public void PublishAlertAcknowledged_NoSubscribers_DoesNotThrow()
  {
    _sut.PublishAlertAcknowledged("SYS-01");
  }

  [TestMethod]
  public void PublishAlertAcknowledged_HandlerThrows_DoesNotPropagate()
  {
    _sut.AlertAcknowledged += _ =>
        throw new InvalidOperationException("subscriber failure");

    _sut.PublishAlertAcknowledged("SYS-01");
  }

  [TestMethod]
  public void PublishAlertAcknowledged_MultipleSubscribers_AllInvoked()
  {
    int callCount = 0;
    _sut.AlertAcknowledged += _ => callCount++;
    _sut.AlertAcknowledged += _ => callCount++;

    _sut.PublishAlertAcknowledged("S");

    Assert.AreEqual(2, callCount);
  }

  // ── PublishMaintenanceModeChanged ─────────────────────────────────────────

  [TestMethod]
  [DataRow(true)]
  [DataRow(false)]
  public void PublishMaintenanceModeChanged_WithSubscriber_InvokesHandler(bool isActive)
  {
    // Arrange
    string? capturedSystem = null;
    bool? capturedActive = null;

    _sut.MaintenanceModeChanged += (sys, active) =>
    {
      capturedSystem = sys;
      capturedActive = active;
    };

    // Act
    _sut.PublishMaintenanceModeChanged("SYS-01", isActive);

    // Assert
    Assert.AreEqual("SYS-01", capturedSystem);
    Assert.AreEqual(isActive, capturedActive);
  }

  [TestMethod]
  public void PublishMaintenanceModeChanged_NoSubscribers_DoesNotThrow()
  {
    _sut.PublishMaintenanceModeChanged("SYS-01", true);
  }

  [TestMethod]
  public void PublishMaintenanceModeChanged_HandlerThrows_DoesNotPropagate()
  {
    _sut.MaintenanceModeChanged += (_, _) =>
        throw new InvalidOperationException("subscriber failure");

    _sut.PublishMaintenanceModeChanged("SYS-01", false);
  }

  [TestMethod]
  public void PublishMaintenanceModeChanged_MultipleSubscribers_AllInvoked()
  {
    int callCount = 0;
    _sut.MaintenanceModeChanged += (_, _) => callCount++;
    _sut.MaintenanceModeChanged += (_, _) => callCount++;

    _sut.PublishMaintenanceModeChanged("S", true);

    Assert.AreEqual(2, callCount);
  }

  // ── First-subscriber-throws: second subscriber still invoked? ─────────────
  // NOTE: C# multicast delegate stops at the throwing subscriber — MonitorBroadcaster
  // wraps the entire invocation list in one try-catch, so only the *first* subscriber
  // is guaranteed to run when it throws.  These tests document that contract explicitly.

  [TestMethod]
  public void PublishComponentStatusChanged_FirstHandlerThrows_SecondHandlerIsNotInvoked()
  {
    // Arrange — the broadcaster wraps the whole invocation in a single try/catch,
    // meaning if the first subscriber throws the second will NOT run (documented behavior).
    int secondCallCount = 0;
    _sut.ComponentStatusUpdated += (_, _, _) =>
        throw new InvalidOperationException("first subscriber fails");
    _sut.ComponentStatusUpdated += (_, _, _) => secondCallCount++;

    // Act
    _sut.PublishComponentStatusChanged("C", "S", ComponentStatus.Error);

    // Assert — exception is swallowed, second handler not called (multicast delegate semantics)
    Assert.AreEqual(0, secondCallCount,
        "The second handler should not be invoked when the first subscriber throws, " +
        "because MonitorBroadcaster uses a single try/catch around the event invocation.");
  }
}
