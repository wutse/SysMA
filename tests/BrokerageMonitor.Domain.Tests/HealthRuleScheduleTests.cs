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

    // ── MatchesCron robustness ────────────────────────────────────────────────

    [TestMethod]
    public void IsMatch_CronWithStepExpression_MatchesEverySecondDay()
    {
        // "0 0 */2 * *" — every other day; day 1, 3, 5... should match
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, "0 0 */2 * *");
        var match = new DateOnly(2026, 4, 3);   // day=3 → 1+2=3 → matches
        var noMatch = new DateOnly(2026, 4, 4);   // day=4 → not in 1,3,5,…

        Assert.IsTrue(schedule.IsMatch(match));
        Assert.IsFalse(schedule.IsMatch(noMatch));
    }

    [TestMethod]
    public void IsMatch_CronWithExplicitRangeStart_MatchesFromRangeStart()
    {
        // "0 0 10/5 * *" — day 10,15,20,25,30
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, "0 0 10/5 * *");
        var match = new DateOnly(2026, 4, 15);
        var noMatch = new DateOnly(2026, 4, 12);

        Assert.IsTrue(schedule.IsMatch(match));
        Assert.IsFalse(schedule.IsMatch(noMatch));
    }

    [TestMethod]
    public void IsMatch_CronWithZeroStep_DoesNotMatch()
    {
        // "0 0 */0 * *" — step=0 is invalid; must not hang or throw
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, "0 0 */0 * *");

        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 1)));
    }

    [TestMethod]
    public void Constructor_CronWithAlphabeticField_ThrowsArgumentException()
    {
        // "0 0 X/5 * *" — alphabetic range start is not supported; must fail at construction
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Cron, "0 0 X/5 * *"));
    }

    [TestMethod]
    public void Constructor_CronWithQuestionMark_ThrowsArgumentException()
    {
        // "0 30 17 ? * MON-FRI" — Quartz-specific '?' token is unsupported; also has 6 fields, so we test ? separately
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Cron, "0 17 ? * *"));
    }

    [TestMethod]
    [DataRow("L", "last-day token")]
    [DataRow("5W", "nearest-weekday token")]
    [DataRow("2#3", "nth-weekday token")]
    public void Constructor_CronWithUnsupportedQuartzToken_ThrowsArgumentException(
        string token, string description)
    {
        // e.g. "0 0 L * *" — unsupported Quartz tokens must be rejected at construction
        var expression = $"0 0 {token} * *";

        Assert.Throws<ArgumentException>(
            () => new HealthRuleSchedule(ScheduleType.Cron, expression),
            $"Expected exception for unsupported token: {description}");
    }

    [TestMethod]
    public void Constructor_CronWithTooFewFields_ThrowsArgumentException()
    {
        // 4-field expression — not 5 fields; should be rejected
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Cron, "0 * * *"));
    }

    [TestMethod]
    public void Constructor_CronWithTooManyFields_ThrowsArgumentException()
    {
        // 6-field expression (Quartz-style with seconds) — not supported
        Assert.Throws<ArgumentException>(() =>
            new HealthRuleSchedule(ScheduleType.Cron, "0 0 30 17 * *"));
    }

    [TestMethod]
    public void IsMatch_CronWithMonthScopedExpression_MatchesCorrectly()
    {
        // "0 17 31 1,3,5,7,8,10,12 *" — 31st of months that have 31 days
        var schedule = new HealthRuleSchedule(ScheduleType.Cron, "0 17 31 1,3,5,7,8,10,12 *");

        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 1, 31)));   // Jan 31
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 3, 31)));   // Mar 31
        Assert.IsTrue(schedule.IsMatch(new DateOnly(2026, 12, 31)));  // Dec 31
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 2, 28)));  // Feb — not in month list
        Assert.IsFalse(schedule.IsMatch(new DateOnly(2026, 4, 30)));  // Apr — day 30, not 31
    }
}
