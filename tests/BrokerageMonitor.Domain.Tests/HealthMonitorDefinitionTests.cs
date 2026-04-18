using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class HealthMonitorDefinitionTests
{
    private static HealthRuleSchedule DailySchedule => new(ScheduleType.Daily);
    private static WatchedComponent[] OneComponent =>
        [new WatchedComponent("COMP-01", ComponentType.Service)];

    [TestMethod]
    public void Constructor_EmptyWatchedComponents_ThrowsArgumentException()
    {
        // Arrange — BI-014: WatchedComponents must not be empty
        WatchedComponent[] empty = [];

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new HealthMonitorDefinition(
                Guid.NewGuid(), "SYS-01", "Daily Check",
                new TimeOnly(17, 0), DailySchedule, empty));

        Assert.Contains("BI-014", ex.Message);
    }

    [TestMethod]
    public void Constructor_ValidArguments_CreatesInstance()
    {
        // Arrange & Act
        var def = new HealthMonitorDefinition(
            Guid.NewGuid(), "SYS-01", "Daily Check",
            new TimeOnly(17, 0), DailySchedule, OneComponent);

        // Assert
        Assert.AreEqual("Daily Check", def.Name);
        Assert.HasCount(1, def.WatchedComponents);
        Assert.IsTrue(def.IsActive);
    }

    [TestMethod]
    public void SetWatchedComponents_EmptyList_ThrowsArgumentException()
    {
        // Arrange
        var def = new HealthMonitorDefinition(
            Guid.NewGuid(), "SYS-01", "Daily Check",
            new TimeOnly(17, 0), DailySchedule, OneComponent);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => def.SetWatchedComponents([]));
    }

    [TestMethod]
    public void SetWatchedComponents_ValidList_UpdatesComponents()
    {
        // Arrange
        var def = new HealthMonitorDefinition(
            Guid.NewGuid(), "SYS-01", "Daily Check",
            new TimeOnly(17, 0), DailySchedule, OneComponent);

        WatchedComponent[] newComponents =
        [
            new WatchedComponent("COMP-02", ComponentType.ScheduledJob),
            new WatchedComponent("COMP-03", ComponentType.Service)
        ];

        // Act
        def.SetWatchedComponents(newComponents);

        // Assert
        Assert.HasCount(2, def.WatchedComponents);
    }

    [TestMethod]
    public void Constructor_EmptyDefinitionId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new HealthMonitorDefinition(
                Guid.Empty, "SYS-01", "Daily Check",
                new TimeOnly(17, 0), DailySchedule, OneComponent));
    }

    [TestMethod]
    public void EmailRecipients_IndependentFromSystemAlertRecipients_CanBeSetSeparately()
    {
        // Arrange — BI-010: EmailRecipients completely independent
        var def = new HealthMonitorDefinition(
            Guid.NewGuid(), "SYS-01", "Daily Check",
            new TimeOnly(17, 0), DailySchedule, OneComponent);

        // Act
        def.SetEmailRecipients([new EmailAddress("health@broker.com")]);

        // Assert
        Assert.HasCount(1, def.EmailRecipients);
    }
}
