using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class NotificationInboxItemTests
{
  // -----------------------------------------------------------------------
  // Constructor ??guard clauses
  // -----------------------------------------------------------------------

  [TestMethod]
  public void Constructor_EmptyInboxItemId_ThrowsArgumentException()
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new NotificationInboxItem(
            Guid.Empty, Guid.NewGuid(), null,
            "Title", "Body", NotificationType.HealthSuccess, DateTimeOffset.UtcNow));
  }

  [TestMethod]
  public void Constructor_EmptyDefinitionId_ThrowsArgumentException()
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new NotificationInboxItem(
            Guid.NewGuid(), Guid.Empty, null,
            "Title", "Body", NotificationType.HealthSuccess, DateTimeOffset.UtcNow));
  }

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  public void Constructor_BlankTitle_ThrowsArgumentException(string title)
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new NotificationInboxItem(
            Guid.NewGuid(), Guid.NewGuid(), null,
            title, "Body", NotificationType.HealthSuccess, DateTimeOffset.UtcNow));
  }

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  public void Constructor_BlankBody_ThrowsArgumentException(string body)
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new NotificationInboxItem(
            Guid.NewGuid(), Guid.NewGuid(), null,
            "Title", body, NotificationType.HealthSuccess, DateTimeOffset.UtcNow));
  }

  // -----------------------------------------------------------------------
  // Constructor ??valid construction
  // -----------------------------------------------------------------------

  [TestMethod]
  public void Constructor_ValidArguments_SetsPropertiesCorrectly()
  {
    // Arrange
    var inboxItemId = Guid.NewGuid();
    var definitionId = Guid.NewGuid();
    var executionId = Guid.NewGuid();
    var sentAt = DateTimeOffset.UtcNow;

    // Act
    var item = new NotificationInboxItem(
        inboxItemId, definitionId, executionId,
        "Alert Title", "Alert Body", NotificationType.HealthSuccess, sentAt);

    // Assert
    Assert.AreEqual(inboxItemId, item.InboxItemId);
    Assert.AreEqual(definitionId, item.DefinitionId);
    Assert.AreEqual(executionId, item.ExecutionId);
    Assert.AreEqual("Alert Title", item.Title);
    Assert.AreEqual("Alert Body", item.Body);
    Assert.AreEqual(NotificationType.HealthSuccess, item.NotificationType);
    Assert.AreEqual(sentAt, item.SentAt);
  }

  [TestMethod]
  public void Constructor_NullExecutionId_IsPermitted()
  {
    // ExecutionId is nullable ??alerts have no associated execution
    var item = new NotificationInboxItem(
        Guid.NewGuid(), Guid.NewGuid(), null,
        "Title", "Body", NotificationType.HealthSuccess, DateTimeOffset.UtcNow);

    Assert.IsNull(item.ExecutionId);
  }

  // -----------------------------------------------------------------------
  // IsRead ??default state
  // -----------------------------------------------------------------------

  [TestMethod]
  public void Constructor_IsReadDefaultsFalse()
  {
    var item = BuildItem();

    Assert.IsFalse(item.IsRead);
  }

  // -----------------------------------------------------------------------
  // MarkAsRead ??happy path
  // -----------------------------------------------------------------------

  [TestMethod]
  public void MarkAsRead_WhenUnread_SetsIsReadTrue()
  {
    // Arrange
    var item = BuildItem();

    // Act
    item.MarkAsRead();

    // Assert
    Assert.IsTrue(item.IsRead);
  }

  // -----------------------------------------------------------------------
  // MarkAsRead ??idempotency (calling twice must not throw or flip state back)
  // -----------------------------------------------------------------------

  [TestMethod]
  public void MarkAsRead_CalledTwice_RemainsRead()
  {
    // Arrange
    var item = BuildItem();
    item.MarkAsRead();

    // Act ??second call should be idempotent
    item.MarkAsRead();

    // Assert
    Assert.IsTrue(item.IsRead, "IsRead should stay true after a second MarkAsRead call.");
  }

  // -----------------------------------------------------------------------
  // Helpers
  // -----------------------------------------------------------------------

  private static NotificationInboxItem BuildItem() =>
      new(Guid.NewGuid(), Guid.NewGuid(), null,
          "Test Title", "Test Body", NotificationType.HealthSuccess, DateTimeOffset.UtcNow);
}
