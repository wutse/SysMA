using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class AlertRecordTests
{
    [TestMethod]
    public void Constructor_ValidArguments_CreatesWithGlobalFlagActive()
    {
        // Arrange & Act
        var alert = new AlertRecord(
            Guid.NewGuid(), "SYS-01", "COMP-01",
            ComponentStatus.Lost, DateTimeOffset.UtcNow);

        // Assert
        Assert.IsTrue(alert.IsGlobalFlagActive);
        Assert.IsFalse(alert.IsAcknowledged);
    }

    [TestMethod]
    public void Acknowledge_ValidOperator_ClearsGlobalFlag()
    {
        // Arrange
        var alert = new AlertRecord(
            Guid.NewGuid(), "SYS-01", "COMP-01",
            ComponentStatus.Lost, DateTimeOffset.UtcNow);

        // Act
        alert.Acknowledge("operator1", DateTimeOffset.UtcNow);

        // Assert
        Assert.IsFalse(alert.IsGlobalFlagActive);
        Assert.IsTrue(alert.IsAcknowledged);
        Assert.AreEqual("operator1", alert.AcknowledgedBy);
        Assert.IsNotNull(alert.AcknowledgedAt);
    }

    [TestMethod]
    public void Acknowledge_EmptyOperatorName_ThrowsArgumentException()
    {
        // Arrange — BI-009
        var alert = new AlertRecord(
            Guid.NewGuid(), "SYS-01", "COMP-01",
            ComponentStatus.Error, DateTimeOffset.UtcNow);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => alert.Acknowledge("", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_EmptyAlertId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new AlertRecord(Guid.Empty, "SYS-01", "COMP-01",
                ComponentStatus.Lost, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Constructor_EmptySystemId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new AlertRecord(Guid.NewGuid(), "", "COMP-01",
                ComponentStatus.Lost, DateTimeOffset.UtcNow));
    }
}
