using BrokerageMonitor.MailAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;

namespace BrokerageMonitor.MailAgent.ZeroMQ;

/// <summary>
/// Publishes mail relay messages to the ZeroMQ broker via a NetMQ PUB socket.
/// Topic is always <c>mailrelay</c>; payload is UTF-8 JSON.
/// On connection failure, logs at ERROR level; the socket is recreated on the next
/// publish attempt so the polling loop continues.
/// Design Doc §6.2, FR-048.
/// </summary>
public sealed class ZeroMQMailPublisher : IZeroMQMailPublisher
{
    internal const string MailRelayTopic = "mailrelay";

    private readonly string _brokerAddress;
    private readonly ILogger<ZeroMQMailPublisher> _logger;
    private readonly object _lock = new();

    private PublisherSocket? _socket;
    private bool _disposed;

    public ZeroMQMailPublisher(
        IOptions<MailAgentOptions> options,
        ILogger<ZeroMQMailPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _brokerAddress = options.Value.BrokerAddress;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Publish(string jsonPayload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPayload);

        lock (_lock)
        {
            EnsureConnected();

            try
            {
                // Multipart frame: [topic][json payload]
                _socket!
                    .SendMoreFrame(MailRelayTopic)
                    .SendFrame(jsonPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ZeroMQMailPublisher: failed to publish to {BrokerAddress}. Socket will be recreated on next attempt.",
                    _brokerAddress);

                // Dispose the broken socket so it is re-created on the next call.
                DisposeSocket();
            }
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void EnsureConnected()
    {
        if (_socket is not null) return;

        try
        {
            var socket = new PublisherSocket();
            socket.Connect(_brokerAddress);
            _socket = socket;

            _logger.LogInformation(
                "ZeroMQMailPublisher connected PUB socket to {BrokerAddress}.", _brokerAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ZeroMQMailPublisher: failed to connect PUB socket to {BrokerAddress}.",
                _brokerAddress);
            throw;
        }
    }

    private void DisposeSocket()
    {
        try { _socket?.Dispose(); } catch { /* best-effort */ }
        _socket = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            DisposeSocket();
        }
    }
}
