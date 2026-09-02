using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Transport;

public sealed class WebSocketLifecycle
{
    private readonly IWebSocketConnectionFactory _connectionFactory;
    private readonly IWebSocketSubscription _subscription;
    private readonly IInboundFrameSink _inboundFrameSink;
    private readonly IConnectionStateSink _stateSink;
    private readonly ILifecycleClock _clock;
    private readonly IRandomSource _random;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public WebSocketLifecycle(
        IWebSocketConnectionFactory connectionFactory,
        IWebSocketSubscription subscription,
        IInboundFrameSink inboundFrameSink,
        IConnectionStateSink stateSink,
        ILifecycleClock clock,
        IRandomSource random)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        _inboundFrameSink = inboundFrameSink ?? throw new ArgumentNullException(nameof(inboundFrameSink));
        _stateSink = stateSink ?? throw new ArgumentNullException(nameof(stateSink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public async Task RunAsync(WebSocketLifecycleOptions options, CancellationToken stoppingToken)
    {
        ValidateOptions(options);
        if (!await _runGate.WaitAsync(0, stoppingToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A WebSocket lifecycle is already running.");
        }

        var previousState = ConnectionState.Stopped;
        var consecutiveFailures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IWebSocketConnection? connection = null;
                try
                {
                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Connecting, ConnectionStateReason.Started, consecutiveFailures, stoppingToken).ConfigureAwait(false);
                    connection = await _connectionFactory.ConnectAsync(options.Endpoint, stoppingToken).ConfigureAwait(false);
                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Connected, ConnectionStateReason.Connected, consecutiveFailures, stoppingToken).ConfigureAwait(false);
                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Subscribing, ConnectionStateReason.Connected, consecutiveFailures, stoppingToken).ConfigureAwait(false);

                    foreach (var message in _subscription.Messages)
                    {
                        await connection.SendTextAsync(message, stoppingToken).ConfigureAwait(false);
                    }

                    consecutiveFailures = 0;
                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Live, ConnectionStateReason.Subscribed, consecutiveFailures, stoppingToken).ConfigureAwait(false);
                    await ReceiveUntilDisconnectedAsync(connection, options, stoppingToken).ConfigureAwait(false);
                    throw new WebSocketLifecycleException(ConnectionStateReason.RemoteClosed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (WebSocketLifecycleException exception)
                {
                    consecutiveFailures++;
                    if (options.Reconnect.MaximumAttempts > 0 && consecutiveFailures >= options.Reconnect.MaximumAttempts)
                    {
                        previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Faulted, exception.Reason, consecutiveFailures, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.BackingOff, exception.Reason, consecutiveFailures, stoppingToken).ConfigureAwait(false);
                    await _clock.DelayAsync(ReconnectDelayCalculator.Calculate(options.Reconnect, consecutiveFailures, _random), stoppingToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    consecutiveFailures++;
                    if (options.Reconnect.MaximumAttempts > 0 && consecutiveFailures >= options.Reconnect.MaximumAttempts)
                    {
                        previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Faulted, ConnectionStateReason.ConnectFault, consecutiveFailures, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    previousState = await PublishStateAsync(options.Exchange, previousState, ConnectionState.BackingOff, ConnectionStateReason.ConnectFault, consecutiveFailures, stoppingToken).ConfigureAwait(false);
                    await _clock.DelayAsync(ReconnectDelayCalculator.Calculate(options.Reconnect, consecutiveFailures, _random), stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    if (connection is not null)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _ = await PublishStateAsync(options.Exchange, previousState, ConnectionState.Stopped, ConnectionStateReason.Stopped, consecutiveFailures, CancellationToken.None).ConfigureAwait(false);
            _runGate.Release();
        }
    }

    private async Task ReceiveUntilDisconnectedAsync(IWebSocketConnection connection, WebSocketLifecycleOptions options, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var receiveTask = connection.ReceiveAsync(receiveCancellation.Token);
            var livenessTask = _clock.DelayAsync(options.LivenessTimeout, receiveCancellation.Token);
            var completedTask = await Task.WhenAny(receiveTask, livenessTask).ConfigureAwait(false);

            if (completedTask == livenessTask)
            {
                receiveCancellation.Cancel();
                throw new WebSocketLifecycleException(ConnectionStateReason.LivenessTimeout);
            }

            receiveCancellation.Cancel();
            var frame = await receiveTask.ConfigureAwait(false);
            if (frame.IsRemoteClose)
            {
                throw new WebSocketLifecycleException(ConnectionStateReason.RemoteClosed);
            }

            if (frame.Payload.Length > options.MaximumFrameBytes)
            {
                throw new WebSocketLifecycleException(ConnectionStateReason.ReceiveFault);
            }

            try
            {
                await _inboundFrameSink.HandleAsync(options.Exchange, frame.Payload, _clock.UtcNow, _clock.GetStopwatchTimestamp(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A later adapter may report this through metrics. One malformed frame must not tear down a healthy socket.
            }
        }
    }

    private async ValueTask<ConnectionState> PublishStateAsync(Exchange exchange, ConnectionState previous, ConnectionState current, ConnectionStateReason reason, int failures, CancellationToken cancellationToken)
    {
        var change = new ConnectionStateChanged(exchange, previous, current, reason, failures, _clock.UtcNow);
        try
        {
            await _stateSink.PublishAsync(change, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // State observation cannot terminate market-data recovery.
        }

        return current;
    }

    private static void ValidateOptions(WebSocketLifecycleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Endpoint.IsAbsoluteUri || !string.Equals(options.Endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase) ||
            options.LivenessTimeout <= TimeSpan.Zero || options.MaximumFrameBytes is < 1 or > 1_048_576 ||
            options.Reconnect.InitialDelay <= TimeSpan.Zero || options.Reconnect.MaximumDelay < options.Reconnect.InitialDelay ||
            options.Reconnect.Multiplier is < 1.1 or > 3.0 || options.Reconnect.JitterFraction is < 0 or > 0.5 || options.Reconnect.MaximumAttempts is < 0 or > 100)
        {
            throw new ArgumentException("WebSocket lifecycle options are invalid.", nameof(options));
        }
    }

    private sealed class WebSocketLifecycleException(ConnectionStateReason reason) : Exception
    {
        public ConnectionStateReason Reason { get; } = reason;
    }
}
