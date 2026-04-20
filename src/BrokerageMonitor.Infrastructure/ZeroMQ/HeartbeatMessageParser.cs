using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerageMonitor.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>
/// Parses raw UTF-8 JSON ZeroMQ payload bytes into a <see cref="HeartbeatMessage"/> DTO.
/// Validates all required fields; logs a WARNING and returns <see langword="null"/> on any
/// parse or validation failure so the caller can safely discard the message.
/// US-023.
/// </summary>
public sealed class HeartbeatMessageParser : IHeartbeatMessageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly ILogger<HeartbeatMessageParser> _logger;

    public HeartbeatMessageParser(ILogger<HeartbeatMessageParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public HeartbeatMessage? Parse(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
        {
            _logger.LogWarning("HeartbeatMessageParser received null or empty payload — discarding.");
            return null;
        }

        HeartbeatMessageRaw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<HeartbeatMessageRaw>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "HeartbeatMessageParser failed to deserialize JSON payload — discarding.");
            return null;
        }

        if (raw is null)
        {
            _logger.LogWarning("HeartbeatMessageParser deserialized null from payload — discarding.");
            return null;
        }

        if (!ValidateRequired(raw, out var validationError))
        {
            _logger.LogWarning("HeartbeatMessageParser validation failed: {Error} — discarding.", validationError);
            return null;
        }

        return ToMessage(raw);
    }

    private static bool ValidateRequired(HeartbeatMessageRaw raw, out string error)
    {
        if (string.IsNullOrWhiteSpace(raw.MessageType))
        {
            error = "messageType is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw.SystemId))
        {
            error = "systemId is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw.ComponentId))
        {
            error = "componentId is required";
            return false;
        }

        if (raw.Timestamp == default)
        {
            error = "timestamp is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw.Status))
        {
            error = "status is required";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static HeartbeatMessage ToMessage(HeartbeatMessageRaw raw)
    {
        IReadOnlyList<SubIndicatorPayload>? subIndicators = null;

        if (raw.SubIndicators is { Count: > 0 })
        {
            var list = new List<SubIndicatorPayload>(raw.SubIndicators.Count);
            foreach (var si in raw.SubIndicators)
            {
                MetricPayload? metric = si.Metric is null
                    ? null
                    : new MetricPayload(si.Metric.Label ?? string.Empty, si.Metric.Value);

                list.Add(new SubIndicatorPayload(
                    si.Name ?? string.Empty,
                    si.Status ?? string.Empty,
                    metric));
            }
            subIndicators = list;
        }

        return new HeartbeatMessage(
            raw.MessageType!,
            raw.SystemId!,
            raw.ComponentId!,
            raw.Timestamp,
            raw.Status!,
            raw.Message,
            subIndicators);
    }

    // ---------------------------------------------------------------
    // Internal deserialization shape (camelCase JSON → PascalCase DTO)
    // ---------------------------------------------------------------
    private sealed class HeartbeatMessageRaw
    {
        public string? MessageType { get; set; }
        public string? SystemId { get; set; }
        public string? ComponentId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? Status { get; set; }
        public string? Message { get; set; }
        public List<SubIndicatorRaw>? SubIndicators { get; set; }
    }

    private sealed class SubIndicatorRaw
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public MetricRaw? Metric { get; set; }
    }

    private sealed class MetricRaw
    {
        public string? Label { get; set; }
        public decimal Value { get; set; }
    }
}
