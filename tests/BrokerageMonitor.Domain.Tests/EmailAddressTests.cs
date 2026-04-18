using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class EmailAddressTests
{
    [TestMethod]
    [DataRow("user@example.com")]
    [DataRow("first.last@broker.co.jp")]
    [DataRow("USER@DOMAIN.COM")]
    public void Constructor_ValidEmail_CreatesInstance(string email)
    {
        // Act
        var address = new EmailAddress(email);

        // Assert
        Assert.AreEqual(email.Trim().ToLowerInvariant(), address.Value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Constructor_EmptyValue_ThrowsArgumentException(string email)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new EmailAddress(email));
    }

    [TestMethod]
    [DataRow("notanemail")]
    [DataRow("missing@domain")]
    [DataRow("@nodomain.com")]
    [DataRow("spaces in@email.com")]
    public void Constructor_InvalidFormat_ThrowsArgumentException(string email)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new EmailAddress(email));
    }

    [TestMethod]
    public void Equals_SameEmailDifferentCase_ReturnsTrue()
    {
        // Arrange
        var a = new EmailAddress("User@Example.COM");
        var b = new EmailAddress("user@example.com");

        // Assert
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ToString_ReturnsLowercaseValue()
    {
        // Arrange
        var address = new EmailAddress("USER@EXAMPLE.COM");

        // Assert
        Assert.AreEqual("user@example.com", address.ToString());
    }
}
