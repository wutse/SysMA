using System.Text;
using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetMQ;
using NetMQ.Sockets;

namespace BrokerageMonitor.Infrastructure.ZeroMQ;

/// <summary>
/// Hosted service that connects an XSUB socket to the ZeroMQ broker, receives multipart
/// messages, and dispatches them to <see cref="IHeartbeatProcessor"/> or
/// <see cref="IMailChannelProcessor"/> based on the topic frame.
/// Automatically reconnects with exponential backoff (1 s → 2 s → … → 60 s max) on failure.
/// Logs <c>ERR_ZMQ_DISCONNECTED</c> at ERROR level on each disconnection.
/// FR-002, FR-050, FR-051.
/// </summary>
public sealed class ZeroMQSubscriberService : BackgroundService
{
    internal const string ErrorCodeDisconnected = "ERR_ZMQ_DISCONNECTED";
    internal const string MailRelayTopic = "mailrelay";

    private const int InitialRetrySeconds = 1;
    private const int MaxRetrySeconds = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ZeroMqOptions _options;
    private readonly ILogger<ZeroMQSubscriberService> _logger;

    public ZeroMQSubscriberService(
        IServiceScopeFactory scopeFactory,
        IOptions<ZeroMqOptions> options,
        ILogger<ZeroMQSubscriberService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Offload the blocking receive loop to a thread-pool thread so the host
        // startup is not delayed.
        return Task.Run(() => RunWithReconnectAsync(stoppingToken), stoppingToken);
    }

    private async Task RunWithReconnectAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(InitialRetrySeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReceiveLoopAsync(ct).ConfigureAwait(false);
                // Normal exit (cancellation requested) — stop retrying.
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{ErrorCode}: ZeroMQ connection to {BrokerAddress} lost. Reconnecting in {DelaySeconds} s.",
                    ErrorCodeDisconnected,
                    _options.BrokerAddress,
                    retryDelay.TotalSeconds);

                await Task.Delay(retryDelay, ct).ConfigureAwait(false);

                retryDelay = TimeSpan.FromSeconds(
                    Math.Min(retryDelay.TotalSeconds * 2, MaxRetrySeconds));
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        using var socket = new XSubscriberSocket();
        socket.Connect(_options.BrokerAddress);

        // Subscribe to all topics: XSUB subscription frame = 0x01 prefix + empty topic.
        socket.SendFrame(new byte[] { 0x01 });

        _logger.LogInformation(
            "ZeroMQSubscriberService connected to {BrokerAddress}.",
            _options.BrokerAddress);

        var msg = new NetMQMessage();

        while (!ct.IsCancellationRequested)
        {
            if (socket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(500), ref msg, 2))
            {
                await DispatchAsync(msg, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Determines the message type from the topic frame and dispatches to the
    /// appropriate processor using a short-lived DI scope.
    /// Internal visibility allows unit-testing dispatch logic without a real broker.
    /// </summary>
    internal async Task DispatchAsync(NetMQMessage msg, CancellationToken ct)
    {
        if (msg.FrameCount < 2)
        {
            _logger.LogWarning("Received ZeroMQ message with fewer than 2 frames — discarding.");
            return;
        }

        var topic = msg[0].ConvertToString(Encoding.UTF8);
        var payloadBytes = msg[1].Buffer;

        await using var scope = _scopeFactory.CreateAsyncScope();

        if (string.Equals(topic, MailRelayTopic, StringComparison.OrdinalIgnoreCase))
        {
            var processor = scope.ServiceProvider.GetRequiredService<IMailChannelProcessor>();
            var parser = scope.ServiceProvider.GetRequiredService<IMailChannelMessageParser>();

            var message = parser.Parse(payloadBytes);
            if (message is null)
            {
                return;
            }

            await processor.ProcessAsync(message, ct).ConfigureAwait(false);
        }
        else
        {
            var processor = scope.ServiceProvider.GetRequiredService<IHeartbeatProcessor>();
            var parser = scope.ServiceProvider.GetRequiredService<IHeartbeatMessageParser>();

            var message = parser.Parse(payloadBytes);
            if (message is null)
            {
                return;
            }

            await processor.ProcessAsync(message, ct).ConfigureAwait(false);
        }
    }
}
