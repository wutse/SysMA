using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class MonitoredSystemTests
{
    private static MarketSessionWindow DefaultSession =>
        new(new TimeOnly(9, 0), new TimeOnly(17, 30));

    [TestMethod]
    public void Constructor_ValidArguments_CreatesInstance()
    {
        // Arrange & Act
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);

        // Assert
        Assert.AreEqual("SYS-01", system.SystemId);
        Assert.AreEqual("Test System", system.Name);
        Assert.IsTrue(system.IsActive);
        Assert.IsFalse(system.IsMaintenanceActive);
    }

    [TestMethod]
    public void ActivateMaintenance_WithOperatorName_SetsMaintenance()
    {
        // Arrange
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);

        // Act
        system.ActivateMaintenance("operator1");

        // Assert
        Assert.IsTrue(system.IsMaintenanceActive);
        Assert.AreEqual("operator1", system.MaintenanceOperator);
    }

    [TestMethod]
    public void ActivateMaintenance_EmptyOperatorName_ThrowsArgumentException()
    {
        // Arrange — BI-009: operator name required
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => system.ActivateMaintenance(""));
    }

    [TestMethod]
    public void DeactivateMaintenance_AfterActivation_ClearsMaintenance()
    {
        // Arrange
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);
        system.ActivateMaintenance("operator1");

        // Act
        system.DeactivateMaintenance("operator2");

        // Assert
        Assert.IsFalse(system.IsMaintenanceActive);
        Assert.IsNull(system.MaintenanceOperator);
    }

    [TestMethod]
    public void DeactivateMaintenance_EmptyOperatorName_ThrowsArgumentException()
    {
        // Arrange — BI-009
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => system.DeactivateMaintenance("   "));
    }

    [TestMethod]
    public void Constructor_EmptySystemId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MonitoredSystem("", "Test System", DefaultSession));
    }

    [TestMethod]
    public void SetAlertRecipients_ValidEmails_UpdatesRecipients()
    {
        // Arrange
        var system = new MonitoredSystem("SYS-01", "Test System", DefaultSession);

        // Act
        system.SetAlertRecipients([new EmailAddress("ops@broker.com")]);

        // Assert
        Assert.HasCount(1, system.AlertRecipients);
    }
}
