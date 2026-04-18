using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class MailParsingRuleTests
{
    [TestMethod]
    public void Constructor_ValidArguments_CreatesInstance()
    {
        // Arrange & Act
        var rule = new MailParsingRule(
            fromPattern: "sender@broker.com",
            subjectPattern: "Daily Report",
            successKeywords: ["SUCCESS", "COMPLETED"],
            failureKeywords: ["FAILED", "ERROR"]);

        // Assert
        Assert.AreEqual("sender@broker.com", rule.FromPattern);
        Assert.AreEqual("Daily Report", rule.SubjectPattern);
        Assert.HasCount(2, rule.SuccessKeywords);
        Assert.HasCount(2, rule.FailureKeywords);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Constructor_EmptyFromPattern_ThrowsArgumentException(string fromPattern)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MailParsingRule(fromPattern, "Subject", ["SUCCESS"], ["FAILED"]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Constructor_EmptySubjectPattern_ThrowsArgumentException(string subjectPattern)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MailParsingRule("from@test.com", subjectPattern, ["SUCCESS"], ["FAILED"]));
    }

    [TestMethod]
    public void Constructor_OverlappingKeywords_ThrowsArgumentException()
    {
        // Arrange — BI-016: success and failure keywords must not overlap
        string[] successKeywords = ["COMPLETED", "SUCCESS"];
        string[] failureKeywords = ["FAILED", "COMPLETED"]; // "COMPLETED" overlaps

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new MailParsingRule("from@test.com", "Subject", successKeywords, failureKeywords));

        Assert.Contains("COMPLETED", ex.Message);
    }

    [TestMethod]
    public void Constructor_OverlappingKeywordsCaseInsensitive_ThrowsArgumentException()
    {
        // Arrange — BI-016: case-insensitive overlap check
        string[] successKeywords = ["success"];
        string[] failureKeywords = ["SUCCESS"];

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MailParsingRule("from@test.com", "Subject", successKeywords, failureKeywords));
    }

    [TestMethod]
    public void Constructor_NullSuccessKeywords_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MailParsingRule("from@test.com", "Subject", null!, ["FAILED"]));
    }

    [TestMethod]
    public void Constructor_EmptyKeywordLists_CreatesInstance()
    {
        // Arrange & Act — empty lists are valid (no overlap possible)
        var rule = new MailParsingRule("from@test.com", "Subject", [], []);

        // Assert
        Assert.IsNotNull(rule);
        Assert.IsEmpty(rule.SuccessKeywords);
        Assert.IsEmpty(rule.FailureKeywords);
    }
}
