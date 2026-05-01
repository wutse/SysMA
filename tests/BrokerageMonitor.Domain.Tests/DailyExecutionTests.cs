using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class DailyExecutionTests
{
    private static DailyExecution CreateInProgress() =>
        new(
            executionId: Guid.NewGuid(),
            definitionId: Guid.NewGuid(),
            systemId: "SYS-01",
            executionDate: new DateOnly(2026, 4, 20));

    [TestMethod]
    public void Complete_ValidTerminalStatus_TransitionsToTerminal()
    {
        // Arrange
        var execution = CreateInProgress();

        // Act
        execution.Complete(DailyExecutionStatus.Success, DateTimeOffset.UtcNow);

        // Assert
        Assert.AreEqual(DailyExecutionStatus.Success, execution.Status);
        Assert.IsTrue(execution.IsTerminal);
        Assert.IsNotNull(execution.EvaluatedAt);
    }

    [TestMethod]
    public void Complete_AlreadyTerminal_ThrowsInvalidOperationException()
    {
        // Arrange — BI-013: terminal state cannot be overwritten
        var execution = CreateInProgress();
        execution.Complete(DailyExecutionStatus.Success, DateTimeOffset.UtcNow);

        // Act & Assert
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            execution.Complete(DailyExecutionStatus.Failed, DateTimeOffset.UtcNow));

        Assert.Contains("terminal state", ex.Message);
    }

    [TestMethod]
    public void Complete_FailedStatus_StoresFailedComponents()
    {
        // Arrange
        var execution = CreateInProgress();
        string[] failedComponents = ["COMP-01", "COMP-02"];

        // Act
        execution.Complete(DailyExecutionStatus.Failed, DateTimeOffset.UtcNow, failedComponents);

        // Assert
        Assert.AreEqual(DailyExecutionStatus.Failed, execution.Status);
        Assert.HasCount(2, execution.FailedComponents);
        Assert.Contains("COMP-01", execution.FailedComponents);
    }

    [TestMethod]
    public void Complete_NonTerminalStatus_ThrowsArgumentException()
    {
        // Arrange
        var execution = CreateInProgress();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            execution.Complete(DailyExecutionStatus.InProgress, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void AddCompletedComponent_NewComponent_AddsToList()
    {
        // Arrange
        var execution = CreateInProgress();

        // Act
        execution.AddCompletedComponent("COMP-01");

        // Assert
        Assert.Contains("COMP-01", execution.CompletedComponents);
    }

    [TestMethod]
    public void AddCompletedComponent_DuplicateComponent_NotAddedTwice()
    {
        // Arrange
        var execution = CreateInProgress();
        execution.AddCompletedComponent("COMP-01");

        // Act — add same component again (case-insensitive)
        execution.AddCompletedComponent("comp-01");

        // Assert
        Assert.HasCount(1, execution.CompletedComponents);
    }

    [TestMethod]
    public void AddCompletedComponent_AfterTerminal_SilentlyIgnored()
    {
        // Arrange
        var execution = CreateInProgress();
        execution.Complete(DailyExecutionStatus.Success, DateTimeOffset.UtcNow);

        // Act — should not throw
        execution.AddCompletedComponent("COMP-01");

        // Assert
        Assert.IsEmpty(execution.CompletedComponents);
    }

    [TestMethod]
    public void Constructor_EmptySystemId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new DailyExecution(Guid.NewGuid(), Guid.NewGuid(), "", new DateOnly(2026, 4, 20)));
    }

    [TestMethod]
    public void Constructor_EmptyExecutionId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new DailyExecution(Guid.Empty, Guid.NewGuid(), "SYS-01", new DateOnly(2026, 4, 20)));
    }

    [TestMethod]
    public void IsTerminal_InProgress_ReturnsFalse()
    {
        // Arrange
        var execution = CreateInProgress();

        // Assert
        Assert.IsFalse(execution.IsTerminal);
    }

    [TestMethod]
    [DataRow(DailyExecutionStatus.Success)]
    [DataRow(DailyExecutionStatus.Failed)]
    [DataRow(DailyExecutionStatus.Missed)]
    [DataRow(DailyExecutionStatus.Exempted)]
    public void IsTerminal_AllTerminalStatuses_ReturnTrue(DailyExecutionStatus status)
    {
        // Arrange
        var execution = CreateInProgress();
        execution.Complete(status, DateTimeOffset.UtcNow);

        // Assert
        Assert.IsTrue(execution.IsTerminal);
    }

    // ---- Rehydrate ----

    [TestMethod]
    public void Rehydrate_AllFields_RestoresExactValues()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var evaluatedAt = new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero);
        var notifSentAt = new DateTimeOffset(2026, 4, 1, 14, 1, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 4, 1);
        string[] completed = ["COMP-01", "COMP-02"];
        string[] failed = ["COMP-03"];

        // Act
        var execution = DailyExecution.Rehydrate(
            executionId, definitionId, "SYS-01", date,
            DailyExecutionStatus.Failed,
            createdAt, evaluatedAt,
            completed, failed,
            "missed window", notifSentAt);

        // Assert
        Assert.AreEqual(executionId, execution.ExecutionId);
        Assert.AreEqual(definitionId, execution.DefinitionId);
        Assert.AreEqual("SYS-01", execution.SystemId);
        Assert.AreEqual(date, execution.ExecutionDate);
        Assert.AreEqual(DailyExecutionStatus.Failed, execution.Status);
        Assert.AreEqual(createdAt, execution.CreatedAt);
        Assert.AreEqual(evaluatedAt, execution.EvaluatedAt);
        Assert.AreEqual(notifSentAt, execution.NotificationSentAt);
        Assert.AreEqual("missed window", execution.MissedReason);
        Assert.HasCount(2, execution.CompletedComponents);
        Assert.HasCount(1, execution.FailedComponents);
        Assert.Contains("COMP-01", execution.CompletedComponents);
        Assert.Contains("COMP-03", execution.FailedComponents);
    }

    [TestMethod]
    public void Rehydrate_NullCollections_ReturnsEmptyLists()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var execution = DailyExecution.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "SYS-01", new DateOnly(2026, 4, 1),
            DailyExecutionStatus.InProgress,
            createdAt, null, null, null, null, null);

        // Assert
        Assert.IsEmpty(execution.CompletedComponents);
        Assert.IsEmpty(execution.FailedComponents);
        Assert.IsNull(execution.EvaluatedAt);
        Assert.IsNull(execution.NotificationSentAt);
        Assert.IsNull(execution.MissedReason);
    }

    [TestMethod]
    public void Rehydrate_CreatedAt_DoesNotUseUtcNow()
    {
        // Arrange — a historical timestamp far in the past
        var historicalCreatedAt = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        // Act
        var execution = DailyExecution.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "SYS-01", new DateOnly(2025, 1, 1),
            DailyExecutionStatus.InProgress,
            historicalCreatedAt, null, null, null, null, null);

        // Assert — persisted timestamp must be preserved exactly
        Assert.AreEqual(historicalCreatedAt, execution.CreatedAt);
    }

    [TestMethod]
    public void Rehydrate_TerminalStatus_IsTerminalReturnsTrue()
    {
        // Arrange & Act
        var execution = DailyExecution.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "SYS-01", new DateOnly(2026, 4, 1),
            DailyExecutionStatus.Success,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null, null, null);

        // Assert
        Assert.IsTrue(execution.IsTerminal);
    }

    [TestMethod]
    public void Complete_DuplicateFailedComponents_DeduplicatesBeforeStoring()
    {
        // Arrange
        var execution = CreateInProgress();
        string[] failedWithDuplicates = ["COMP-01", "COMP-02", "COMP-01", "comp-02"];

        // Act
        execution.Complete(DailyExecutionStatus.Failed, DateTimeOffset.UtcNow, failedWithDuplicates);

        // Assert — only two unique IDs (case-insensitive)
        Assert.HasCount(2, execution.FailedComponents);
        Assert.Contains("COMP-01", execution.FailedComponents);
        Assert.Contains("COMP-02", execution.FailedComponents);
    }

    [TestMethod]
    public void Complete_DuplicatesCaseInsensitive_TreatedAsSameComponent()
    {
        // Arrange
        var execution = CreateInProgress();
        string[] mixedCase = ["COMP-A", "comp-a", "Comp-A"];

        // Act
        execution.Complete(DailyExecutionStatus.Failed, DateTimeOffset.UtcNow, mixedCase);

        // Assert — only one entry for the same logical component
        Assert.HasCount(1, execution.FailedComponents);
    }
}
