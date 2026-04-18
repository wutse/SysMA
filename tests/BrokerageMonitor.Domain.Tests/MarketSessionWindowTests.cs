using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class MarketSessionWindowTests
{
    [TestMethod]
    public void Constructor_ValidTimes_CreatesInstance()
    {
        // Arrange & Act
        var window = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 30));

        // Assert
        Assert.AreEqual(new TimeOnly(9, 0), window.StartTime);
        Assert.AreEqual(new TimeOnly(17, 30), window.EndTime);
    }

    [TestMethod]
    public void Constructor_StartAfterEnd_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MarketSessionWindow(new TimeOnly(17, 0), new TimeOnly(9, 0)));
    }

    [TestMethod]
    public void Constructor_StartEqualsEnd_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(9, 0)));
    }

    [TestMethod]
    public void IsWithinSession_TimeInsideWindow_ReturnsTrue()
    {
        // Arrange
        var window = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 30));

        // Assert
        Assert.IsTrue(window.IsWithinSession(new TimeOnly(12, 0)));
        Assert.IsTrue(window.IsWithinSession(new TimeOnly(9, 0)));
        Assert.IsTrue(window.IsWithinSession(new TimeOnly(17, 30)));
    }

    [TestMethod]
    public void IsWithinSession_TimeOutsideWindow_ReturnsFalse()
    {
        // Arrange
        var window = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 30));

        // Assert
        Assert.IsFalse(window.IsWithinSession(new TimeOnly(8, 59)));
        Assert.IsFalse(window.IsWithinSession(new TimeOnly(17, 31)));
    }

    [TestMethod]
    public void IsWithinSession_DateTimeOffset_UsesTimeComponent()
    {
        // Arrange
        var window = new MarketSessionWindow(new TimeOnly(9, 0), new TimeOnly(17, 30));
        var inSession = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
        var outOfSession = new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero);

        // Assert
        Assert.IsTrue(window.IsWithinSession(inSession));
        Assert.IsFalse(window.IsWithinSession(outOfSession));
    }
}
