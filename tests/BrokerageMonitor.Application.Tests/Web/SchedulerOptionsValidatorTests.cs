using BrokerageMonitor.Web.Services;
using Microsoft.Extensions.Options;

namespace BrokerageMonitor.Application.Tests.Web;

[TestClass]
public sealed class SchedulerOptionsValidatorTests
{
  private static readonly SchedulerOptionsValidator Sut = new();

  // ── Valid expressions ────────────────────────────────────────────────────

  [TestMethod]
  public void Validate_ValidCronExpressions_ReturnsSuccess()
  {
    var options = new SchedulerOptions
    {
      SmokeTestCron = "0 0 1 * * ?",
      DailyExecutionCreatorCron = "0 30 5 * * ?"
    };

    var result = Sut.Validate(null, options);

    Assert.AreEqual(ValidateOptionsResult.Success, result);
  }

  [TestMethod]
  [DataRow("0 0 1 * * ?")]
  [DataRow("0 30 5 * * ?")]
  [DataRow("0 0 12 * * MON-FRI")]
  [DataRow("0 0/15 * * * ?")]
  public void Validate_SmokeTestCron_ValidExpression_ReturnsSuccess(string cron)
  {
    var options = new SchedulerOptions
    {
      SmokeTestCron = cron,
      DailyExecutionCreatorCron = "0 30 5 * * ?"
    };

    var result = Sut.Validate(null, options);

    Assert.AreEqual(ValidateOptionsResult.Success, result);
  }

  // ── Invalid SmokeTestCron ────────────────────────────────────────────────

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  [DataRow("0 0 1 * *")]          // 5 fields — missing day-of-week
  [DataRow("0 0 1 * * ? 2026")]   // 7 fields — year appended
  [DataRow("not-a-cron")]
  public void Validate_SmokeTestCron_InvalidExpression_ReturnsFailure(string badCron)
  {
    var options = new SchedulerOptions
    {
      SmokeTestCron = badCron,
      DailyExecutionCreatorCron = "0 30 5 * * ?"
    };

    var result = Sut.Validate(null, options);

    Assert.IsTrue(result.Failed);
    Assert.IsTrue(result.Failures!.Any(f => f.Contains("SmokeTestCron")));
  }

  // ── Invalid DailyExecutionCreatorCron ────────────────────────────────────

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  [DataRow("0 30 5 * *")]         // 5 fields
  [DataRow("0 30 5 * * ? 2026")]  // 7 fields
  [DataRow("invalid")]
  public void Validate_DailyExecutionCreatorCron_InvalidExpression_ReturnsFailure(string badCron)
  {
    var options = new SchedulerOptions
    {
      SmokeTestCron = "0 0 1 * * ?",
      DailyExecutionCreatorCron = badCron
    };

    var result = Sut.Validate(null, options);

    Assert.IsTrue(result.Failed);
    Assert.IsTrue(result.Failures!.Any(f => f.Contains("DailyExecutionCreatorCron")));
  }

  // ── Both invalid ─────────────────────────────────────────────────────────

  [TestMethod]
  public void Validate_BothCronsInvalid_ReturnsFailureWithTwoMessages()
  {
    var options = new SchedulerOptions
    {
      SmokeTestCron = "bad",
      DailyExecutionCreatorCron = "also-bad"
    };

    var result = Sut.Validate(null, options);

    Assert.IsTrue(result.Failed);
    Assert.AreEqual(2, result.Failures!.Count());
  }
}
