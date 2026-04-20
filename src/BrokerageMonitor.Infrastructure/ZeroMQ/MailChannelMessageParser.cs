using System.Text.Json;
using System.Text.Json.Serialization;
using BrokerageMonitor.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>
/// Parses raw UTF-8 JSON ZeroMQ payload bytes into a <see cref="MailRelayMessage"/> DTO.
/// Validates that <c>messageType == "MailRelay"</c> and all required fields are present.
/// Logs a WARNING and returns <see langword="null"/> on any failure so the caller can discard
/// the message safely.
/// US-059 (partial dependency for ZeroMQSubscriberService dispatch).
/// </summary>
public sealed class MailChannelMessageParser : IMailChannelMessageParser
{
    internal const string ExpectedMessageType = "MailRelay";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<MailChannelMessageParser> _logger;

    public MailChannelMessageParser(ILogger<MailChannelMessageParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public MailRelayMessage? Parse(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
        {
            _logger.LogWarning("MailChannelMessageParser received null or empty payload — discarding.");
            return null;
        }

        MailRelayMessageRaw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<MailRelayMessageRaw>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "MailChannelMessageParser failed to deserialize JSON payload — discarding.");
            return null;
        }

        if (raw is null)
        {
            _logger.LogWarning("MailChannelMessageParser deserialized null from payload — discarding.");
            return null;
        }

        if (!string.Equals(raw.MessageType, ExpectedMessageType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "MailChannelMessageParser: unexpected messageType '{Type}' (expected '{Expected}') — discarding.",
                raw.MessageType,
                ExpectedMessageType);
            return null;
        }

        if (!ValidateRequired(raw, out var validationError))
        {
            _logger.LogWarning("MailChannelMessageParser validation failed: {Error} — discarding.", validationError);
            return null;
        }

        return new MailRelayMessage(
            raw.MessageType!,
            raw.From!,
            raw.Subject!,
            raw.Body ?? string.Empty,
            raw.ReceivedAt);
    }

    private static bool ValidateRequired(MailRelayMessageRaw raw, out string error)
    {
        if (string.IsNullOrWhiteSpace(raw.From))
        {
            error = "from is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw.Subject))
        {
            error = "subject is required";
            return false;
        }

        if (raw.ReceivedAt == default)
        {
            error = "receivedAt is required";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed class MailRelayMessageRaw
    {
        public string? MessageType { get; set; }
        public string? From { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
    }
}
