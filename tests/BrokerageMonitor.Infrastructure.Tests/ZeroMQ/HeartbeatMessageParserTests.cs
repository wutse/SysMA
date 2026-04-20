using System.Text;
using System.Text.Json;
using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Infrastructure.ZeroMQ;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Infrastructure.Tests.ZeroMQ;

[TestClass]
public sealed class HeartbeatMessageParserTests
{
    private readonly HeartbeatMessageParser _parser;

    public HeartbeatMessageParserTests()
    {
        _parser = new HeartbeatMessageParser(NullLogger<HeartbeatMessageParser>.Instance);
    }

    // ---------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------

    private static byte[] ToBytes(object payload) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

    // ---------------------------------------------------------------
    // Happy-path tests
    // ---------------------------------------------------------------

    [TestMethod]
    public void Parse_ValidMinimalJson_ReturnsHeartbeatMessage()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            systemId = "WMM",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Normal",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Heartbeat", result.MessageType);
        Assert.AreEqual("WMM", result.SystemId);
        Assert.AreEqual("svc01", result.ComponentId);
        Assert.AreEqual("Normal", result.Status);
        Assert.IsNull(result.Message);
        Assert.IsNull(result.SubIndicators);
    }

    [TestMethod]
    public void Parse_ValidJsonWithOptionalMessage_SetsMessageField()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "StatusUpdate",
            systemId = "WMM",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Warning",
            message = "High CPU usage",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("High CPU usage", result.Message);
    }

    [TestMethod]
    public void Parse_ValidJsonWithSubIndicators_ParsesSubIndicatorsCorrectly()
    {
        // Arrange — use a raw JSON string to avoid anonymous-type array constraints
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "messageType": "Heartbeat",
              "systemId": "WMM",
              "componentId": "svc01",
              "timestamp": "2026-04-17T08:30:00.000Z",
              "status": "Normal",
              "subIndicators": [
                {
                  "name": "AS400 Connection",
                  "status": "Normal",
                  "metric": { "label": "Received Count", "value": 1234 }
                },
                {
                  "name": "DB Health",
                  "status": "Error",
                  "metric": null
                }
              ]
            }
            """);

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.SubIndicators);
        Assert.HasCount(2, result.SubIndicators);

        var first = result.SubIndicators[0];
        Assert.AreEqual("AS400 Connection", first.Name);
        Assert.AreEqual("Normal", first.Status);
        Assert.IsNotNull(first.Metric);
        Assert.AreEqual("Received Count", first.Metric!.Label);
        Assert.AreEqual(1234m, first.Metric.Value);

        var second = result.SubIndicators[1];
        Assert.AreEqual("DB Health", second.Name);
        Assert.AreEqual("Error", second.Status);
        Assert.IsNull(second.Metric);
    }

    [TestMethod]
    public void Parse_ValidJsonWithEmptySubIndicators_ReturnsNullSubIndicators()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            systemId = "WMM",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Normal",
            subIndicators = Array.Empty<object>(),
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNull(result.SubIndicators);
    }

    // ---------------------------------------------------------------
    // Validation failure tests
    // ---------------------------------------------------------------

    [TestMethod]
    public void Parse_NullPayload_ReturnsNull()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_EmptyPayload_ReturnsNull()
    {
        // Act
        var result = _parser.Parse([]);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_InvalidJson_ReturnsNull()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("this is not json {{{");

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingMessageType_ReturnsNull()
    {
        // Arrange
        var payload = ToBytes(new
        {
            systemId = "WMM",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Normal",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingSystemId_ReturnsNull()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Normal",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingComponentId_ReturnsNull()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            systemId = "WMM",
            timestamp = "2026-04-17T08:30:00.000Z",
            status = "Normal",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingStatus_ReturnsNull()
    {
        // Arrange
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            systemId = "WMM",
            componentId = "svc01",
            timestamp = "2026-04-17T08:30:00.000Z",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_MissingTimestamp_ReturnsNull()
    {
        // Arrange — omit timestamp entirely so it defaults to DateTimeOffset.MinValue
        var payload = ToBytes(new
        {
            messageType = "Heartbeat",
            systemId = "WMM",
            componentId = "svc01",
            status = "Normal",
        });

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_CaseInsensitiveFieldNames_Succeeds()
    {
        // Arrange — field names in non-standard casing
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "MessageType": "Heartbeat",
              "SystemId": "WMM",
              "ComponentId": "svc01",
              "Timestamp": "2026-04-17T08:30:00.000Z",
              "Status": "Normal"
            }
            """);

        // Act
        var result = _parser.Parse(payload);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("WMM", result.SystemId);
    }
}
