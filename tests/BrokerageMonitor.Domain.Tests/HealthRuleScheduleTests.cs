using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class HealthRuleScheduleTests
{
    [TestMethod]
    public void IsMatch_DailySchedule_AlwaysReturnsTrue()
    {
        // Arrange
        var schedule = new HealthRuleSchedule(ScheduleType.Daily);

        // Act & Assert
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 20)));
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 21)));
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 12, 31)));
    }

    [TestMethod]
    public void IsMatch_WeeklySchedule_MatchesOnlySpecifiedDay()
    {
        // Arrange — 2026-04-20 is a Monday
        var schedule = new HealthRuleSchedule(ScheduleType.Weekly, dayOfWeek: DayOfWeek.Monday);

        // Act & Assert
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 20)));  // Monday
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 21))); // Tuesday
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 22))); // Wednesday
    }

    [TestMethod]
    public void IsMatch_CronSchedule_MatchesEveryMondayAtMidnight()
    {
        // Arrange — "0 0 * * 1" = every Monday
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, cronExpression: "0 0 * * 1");

        // Act & Assert — 2026-04-20 is Monday
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 20)));  // Monday
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 21))); // Tuesday
    }

    [TestMethod]
    public void IsMatch_CronSchedule_MatchesSpecificDayOfMonth()
    {
        // Arrange — "0 0 15 * *" = 15th of every month
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, cronExpression: "0 0 15 * *");

        // Act & Assert
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 15)));
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 5, 15)));
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 14)));
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 16)));
    }

    [TestMethod]
    public void IsMatch_CronSchedule_MatchesRangeOfDays()
    {
        // Arrange — "0 0 * * 1-5" = Monday to Friday
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, cronExpression: "0 0 * * 1-5");

        // Act & Assert — 2026-04-20 Monday, 2026-04-25 Saturday
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 20)));  // Monday
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 4, 24)));  // Friday
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 25))); // Saturday
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 26))); // Sunday
    }

    [TestMethod]
    public void Constructor_CronTypeWithNullExpression_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Cron, cronExpression: null));
    }

    [TestMethod]
    public void Constructor_WeeklyTypeWithNullDayOfWeek_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Weekly, dayOfWeek: null));
    }

    [TestMethod]
    public void Constructor_DailyType_DoesNotRequireCronOrDayOfWeek()
    {
        // Act — should not throw
        var schedule = new HealthRuleSchedule(ScheduleType.Daily);
        Assert.AreEqual(ScheduleType.Daily, schedule.ScheduleType);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var s1 = new HealthRuleSchedule(ScheduleType.Weekly, dayOfWeek: DayOfWeek.Monday);
        var s2 = new HealthRuleSchedule(ScheduleType.Weekly, dayOfWeek: DayOfWeek.Monday);

        // Assert
        Assert.AreEqual(s1, s2);
    }
}
