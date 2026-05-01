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

    // ---- GetHashCode ----

    [TestMethod]
    public void GetHashCode_SameKeywords_ReturnsSameHash()
    {
        // Arrange
        var r1 = new MailParsingRule("from@test.com", "Subject", ["OK"], ["FAIL"]);
        var r2 = new MailParsingRule("from@test.com", "Subject", ["OK"], ["FAIL"]);

        // Assert
        Assert.AreEqual(r1.GetHashCode(), r2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_DifferentSuccessKeywords_ReturnsDifferentHash()
    {
        // Arrange — same from/subject but different success keywords
        var r1 = new MailParsingRule("from@test.com", "Subject", ["OK"], ["FAIL"]);
        var r2 = new MailParsingRule("from@test.com", "Subject", ["GOOD"], ["FAIL"]);

        // Assert — probability of collision is negligible; distinct keywords should differ
        Assert.AreNotEqual(r1.GetHashCode(), r2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_DifferentFailureKeywords_ReturnsDifferentHash()
    {
        // Arrange — same from/subject/success but different failure keywords
        var r1 = new MailParsingRule("from@test.com", "Subject", ["OK"], ["FAIL"]);
        var r2 = new MailParsingRule("from@test.com", "Subject", ["OK"], ["ERROR"]);

        // Assert
        Assert.AreNotEqual(r1.GetHashCode(), r2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_ConsistentWithEquals_WhenEqualReturnsSameHash()
    {
        // Arrange
        var r1 = new MailParsingRule("a@b.com", "Re:", ["Pass"], ["Fail"]);
        var r2 = new MailParsingRule("a@b.com", "Re:", ["Pass"], ["Fail"]);

        // Assert — Equals contract: equal objects must have same hash
        Assert.IsTrue(r1.Equals(r2));
        Assert.AreEqual(r1.GetHashCode(), r2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_UseableAsHashSetKey_NoDegradedLookup()
    {
        // Arrange — two rules with same from/subject but different keywords
        var r1 = new MailParsingRule("x@y.com", "Subj", ["A"], ["B"]);
        var r2 = new MailParsingRule("x@y.com", "Subj", ["C"], ["D"]);
        var set = new HashSet<MailParsingRule> { r1, r2 };

        // Assert — both are stored as distinct entries
        Assert.HasCount(2, set);
    }
}
