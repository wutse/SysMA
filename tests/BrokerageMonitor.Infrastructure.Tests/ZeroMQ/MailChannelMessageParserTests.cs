using System.Text;
using System.Text.Json;
using BrokerageMonitor.Infrastructure.ZeroMQ;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Infrastructure.Tests.ZeroMQ;

[TestClass]
public sealed class MailChannelMessageParserTests
{
    private readonly MailChannelMessageParser _parser;

    public MailChannelMessageParserTests()
    {
        _parser = new MailChannelMessageParser(NullLogger<MailChannelMessageParser>.Instance);
    }

    private static byte[] ToBytes(object payload) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

    // ---------------------------------------------------------------
    // Happy-path
    // ---------------------------------------------------------------

    [TestMethod]
    public void Parse_ValidMailRelayJson_ReturnsMailRelayMessage()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "MailRelay",
            from = "scheduler@company.local",
            subject = "[Test] 日結清算排程 執行完成",
            body = "郵件本文",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("MailRelay", result.MessageType);
        Assert.AreEqual("scheduler@company.local", result.From);
        Assert.AreEqual("[Test] 日結清算排程 執行完成", result.Subject);
        Assert.AreEqual("郵件本文", result.Body);
    }

    [TestMethod]
    public void Parse_MessageTypeIsCaseInsensitive_ReturnsMessage()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "mailrelay",
            from = "scheduler@company.local",
            subject = "Test Subject",
            body = "body",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert — messageType comparison is case-insensitive
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Parse_MissingBody_ReturnsMessageWithEmptyBody()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "MailRelay",
            from = "scheduler@company.local",
            subject = "Test",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.Body);
    }

    // ---------------------------------------------------------------
    // Validation failures
    // ---------------------------------------------------------------

    [TestMethod]
    public void Parse_NullPayload_ReturnsNull()
    {
        Assert.IsNull(_parser.Parse(null!));
    }

    [TestMethod]
    public void Parse_EmptyPayload_ReturnsNull()
    {
        Assert.IsNull(_parser.Parse([]));
    }

    [TestMethod]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.IsNull(_parser.Parse(Encoding.UTF8.GetBytes("NOT JSON")));
    }

    [TestMethod]
    public void Parse_WrongMessageType_ReturnsNull()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            from = "x@y.com",
            subject = "Test",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        // Act & Assert
        Assert.IsNull(_parser.Parse(payload));
    }

    [TestMethod]
    public void Parse_MissingFrom_ReturnsNull()
    {
        var payload = ToBytes(new
        {
            messageType = "MailRelay",
            subject = "Test",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        Assert.IsNull(_parser.Parse(payload));
    }

    [TestMethod]
    public void Parse_MissingSubject_ReturnsNull()
    {
        var payload = ToBytes(new
        {
            messageType = "MailRelay",
            from = "x@y.com",
            receivedAt = "2026-04-18T08:35:00.000Z",
        });

        Assert.IsNull(_parser.Parse(payload));
    }

    [TestMethod]
    public void Parse_MissingReceivedAt_ReturnsNull()
    {
        var payload = ToBytes(new
        {
            messageType = "MailRelay",
            from = "x@y.com",
            subject = "Test",
        });

        Assert.IsNull(_parser.Parse(payload));
    }
}
