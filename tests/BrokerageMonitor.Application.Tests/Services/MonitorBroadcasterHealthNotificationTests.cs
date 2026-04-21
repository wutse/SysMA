using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

/// <summary>
/// Unit tests for the <see cref="MonitorBroadcaster.PublishHealthNotificationReceived"/> method — US-044.
/// </summary>
[TestClass]
public sealed class MonitorBroadcasterHealthNotificationTests
{
    private readonly MonitorBroadcaster _sut = new(NullLogger<MonitorBroadcaster>.Instance);

    // ── PublishHealthNotificationReceived ─────────────────────────────────────

    [TestMethod]
    public void PublishHealthNotificationReceived_WithSubscriber_InvokesHandler()
    {
        // Arrange
        Guid? capturedDefinition = null;
        Guid? capturedExecution = null;
        NotificationType? capturedType = null;

        _sut.HealthNotificationReceived += (defId, exId, type) =>
        {
            capturedDefinition = defId;
            capturedExecution = exId;
            capturedType = type;
        };

        var definitionId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        // Act
        _sut.PublishHealthNotificationReceived(definitionId, executionId, NotificationType.HealthSuccess);

        // Assert
        Assert.AreEqual(definitionId, capturedDefinition);
        Assert.AreEqual(executionId, capturedExecution);
        Assert.AreEqual(NotificationType.HealthSuccess, capturedType);
    }

    [TestMethod]
    public void PublishHealthNotificationReceived_NoSubscribers_DoesNotThrow()
    {
        // Act / Assert — must not throw even with no subscribers
        _sut.PublishHealthNotificationReceived(Guid.NewGuid(), Guid.NewGuid(), NotificationType.HealthFailure);
    }

    [TestMethod]
    public void PublishHealthNotificationReceived_HandlerThrows_DoesNotPropagate()
    {
        // Arrange — subscriber that throws
        _sut.HealthNotificationReceived += (_, _, _) => throw new InvalidOperationException("test error");

        // Act / Assert — broadcaster must swallow the exception (FR-020: toast failure must not crash)
        _sut.PublishHealthNotificationReceived(Guid.NewGuid(), Guid.NewGuid(), NotificationType.HealthExempted);
    }

    [TestMethod]
    public void PublishHealthNotificationReceived_MultipleSubscribers_AllInvoked()
    {
        // Arrange
        int callCount = 0;
        _sut.HealthNotificationReceived += (_, _, _) => callCount++;
        _sut.HealthNotificationReceived += (_, _, _) => callCount++;

        // Act
        _sut.PublishHealthNotificationReceived(Guid.NewGuid(), Guid.NewGuid(), NotificationType.HealthSuccess);

        // Assert
        Assert.AreEqual(2, callCount);
    }
}
