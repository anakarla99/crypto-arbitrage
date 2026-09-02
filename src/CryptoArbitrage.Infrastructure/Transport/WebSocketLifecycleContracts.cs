using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Transport;

public enum ConnectionState { Stopped, Connecting, Connected, Subscribing, Live, BackingOff, Faulted }

public enum ConnectionStateReason { Started, Connected, Subscribed, RemoteClosed, ConnectFault, SubscriptionFault, ReceiveFault, LivenessTimeout, AttemptLimitReached, Stopped }

public readonly record struct ConnectionStateChanged(
    Exchange Exchange,
    ConnectionState Previous,
    ConnectionState Current,
    ConnectionStateReason Reason,
    int ConsecutiveFailures,
    DateTimeOffset AtUtc);

public readonly record struct ReceivedFrame(ReadOnlyMemory<byte> Payload, bool IsRemoteClose = false);

public interface IWebSocketConnection : IAsyncDisposable
{
    Task SendTextAsync(string payload, CancellationToken cancellationToken);
    Task<ReceivedFrame> ReceiveAsync(CancellationToken cancellationToken);
}

public interface IWebSocketConnectionFactory
{
    Task<IWebSocketConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken);
}

public interface IWebSocketSubscription
{
    IReadOnlyList<string> Messages { get; }
}

public interface IInboundFrameSink
{
    ValueTask HandleAsync(Exchange exchange, ReadOnlyMemory<byte> payload, DateTimeOffset receivedAtUtc, long receivedAtStopwatchTicks, CancellationToken cancellationToken);
}

public interface IConnectionStateSink
{
    ValueTask PublishAsync(ConnectionStateChanged change, CancellationToken cancellationToken);
}

public interface ILifecycleClock
{
    DateTimeOffset UtcNow { get; }
    long GetStopwatchTimestamp();
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IRandomSource
{
    double NextDouble();
}

public sealed record LifecycleReconnectOptions(TimeSpan InitialDelay, TimeSpan MaximumDelay, double Multiplier, double JitterFraction, int MaximumAttempts);

public sealed record WebSocketLifecycleOptions(
    Exchange Exchange,
    Uri Endpoint,
    LifecycleReconnectOptions Reconnect,
    TimeSpan LivenessTimeout,
    int MaximumFrameBytes);
