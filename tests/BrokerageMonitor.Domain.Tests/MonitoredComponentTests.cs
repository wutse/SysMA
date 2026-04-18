using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class MonitoredComponentTests
{
    [TestMethod]
    public void Constructor_ServiceComponent_CreatesInstance()
    {
        // Arrange & Act
        var component = new MonitoredComponent(
            "COMP-01", "SYS-01", "Trade Service",
            ComponentType.Service, "topic.trade", 60);

        // Assert
        Assert.AreEqual("COMP-01", component.ComponentId);
        Assert.AreEqual(ComponentType.Service, component.ComponentType);
        Assert.IsTrue(component.IsActive);
    }

    [TestMethod]
    public void Constructor_ScheduledJobWithCron_CreatesInstance()
    {
        // Arrange & Act
        var component = new MonitoredComponent(
            "JOB-01", "SYS-01", "EOD Job",
            ComponentType.ScheduledJob, "topic.job", 3600,
            cronExpression: "0 17 * * 1-5");

        // Assert
        Assert.AreEqual("0 17 * * 1-5", component.CronExpression);
    }

    [TestMethod]
    public void Constructor_ScheduledJobWithoutCron_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MonitoredComponent(
                "JOB-01", "SYS-01", "EOD Job",
                ComponentType.ScheduledJob, "topic.job", 3600));
    }

    [TestMethod]
    public void Constructor_ZeroHeartbeatTimeout_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MonitoredComponent(
                "COMP-01", "SYS-01", "Service",
                ComponentType.Service, "topic", 0));
    }

    [TestMethod]
    public void Constructor_EmptyComponentId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MonitoredComponent("", "SYS-01", "Service", ComponentType.Service, "topic", 60));
    }

    [TestMethod]
    public void SetMailParsingRule_WithValidRule_UpdatesRule()
    {
        // Arrange
        var component = new MonitoredComponent(
            "COMP-01", "SYS-01", "Mail Component",
            ComponentType.Service, "topic", 60);

        var rule = new MailParsingRule("from@test.com", "Subject", ["SUCCESS"], ["FAILED"]);

        // Act
        component.SetMailParsingRule(rule);

        // Assert
        Assert.IsNotNull(component.MailParsingRule);
        Assert.AreEqual("from@test.com", component.MailParsingRule.FromPattern);
    }

    [TestMethod]
    public void Deactivate_ActiveComponent_SetsIsActiveFalse()
    {
        // Arrange
        var component = new MonitoredComponent(
            "COMP-01", "SYS-01", "Service", ComponentType.Service, "topic", 60);

        // Act
        component.Deactivate();

        // Assert
        Assert.IsFalse(component.IsActive);
    }
}
