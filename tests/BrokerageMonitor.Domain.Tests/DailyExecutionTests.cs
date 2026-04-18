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
}
