using System.Text;
using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Transport;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class WebSocketLifecycleTests
{
    [Fact]
    public void CalculatesCappedJitteredReconnectDelay()
    {
        var options = new LifecycleReconnectOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 2, 0.2, 0);

        Assert.Equal(TimeSpan.FromSeconds(1.2), ReconnectDelayCalculator.Calculate(options, 1, new FixedRandom(1)));
        Assert.Equal(TimeSpan.FromSeconds(5), ReconnectDelayCalculator.Calculate(options, 4, new FixedRandom(1)));
    }

    [Fact]
    public async Task RemoteCloseDisposesConnectionAndStopsAtAttemptLimit()
    {
        var connection = new FakeConnection(new ReceivedFrame(ReadOnlyMemory<byte>.Empty, true));
        var states = new StateSink();
        var lifecycle = CreateLifecycle(new FakeFactory(connection), states, new ImmediateClock());

        await lifecycle.RunAsync(Options(maximumAttempts: 1), CancellationToken.None);

        Assert.Equal(1, connection.DisposeCount);
        Assert.Single(connection.SentMessages);
        Assert.Contains(states.Changes, change => change.Current == ConnectionState.Live);
        Assert.Contains(states.Changes, change => change.Current == ConnectionState.Faulted && change.Reason == ConnectionStateReason.RemoteClosed);
    }

    [Fact]
    public async Task SilentConnectionTriggersLivenessTimeout()
    {
        var connection = new FakeConnection();
        var states = new StateSink();
        var lifecycle = CreateLifecycle(new FakeFactory(connection), states, new ImmediateClock());

        await lifecycle.RunAsync(Options(maximumAttempts: 1), CancellationToken.None);

        Assert.Equal(1, connection.DisposeCount);
        Assert.Contains(states.Changes, change => change.Current == ConnectionState.Faulted && change.Reason == ConnectionStateReason.LivenessTimeout);
    }

    private static WebSocketLifecycle CreateLifecycle(IWebSocketConnectionFactory factory, IConnectionStateSink states, ILifecycleClock clock) => new(
        factory,
        new Subscription(),
        new Sink(),
        states,
        clock,
        new FixedRandom(0.5));

    private static WebSocketLifecycleOptions Options(int maximumAttempts) => new(
        Exchange.BinanceSpot,
        new Uri("wss://example.test/ws"),
        new LifecycleReconnectOptions(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), 2, 0, maximumAttempts),
        TimeSpan.FromMilliseconds(1),
        1024);

    private sealed class FakeFactory(FakeConnection connection) : IWebSocketConnectionFactory
    {
        public Task<IWebSocketConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.FromResult<IWebSocketConnection>(connection);
    }

    private sealed class FakeConnection(params ReceivedFrame[] frames) : IWebSocketConnection
    {
        private readonly Queue<ReceivedFrame> _frames = new(frames);
        public int DisposeCount { get; private set; }
        public List<string> SentMessages { get; } = [];

        public Task SendTextAsync(string payload, CancellationToken cancellationToken)
        {
            SentMessages.Add(payload);
            return Task.CompletedTask;
        }

        public Task<ReceivedFrame> ReceiveAsync(CancellationToken cancellationToken) =>
            _frames.Count > 0 ? Task.FromResult(_frames.Dequeue()) : new TaskCompletionSource<ReceivedFrame>().Task;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Subscription : IWebSocketSubscription
    {
        public IReadOnlyList<string> Messages { get; } = ["subscribe"];
    }

    private sealed class Sink : IInboundFrameSink
    {
        public ValueTask HandleAsync(Exchange exchange, ReadOnlyMemory<byte> payload, DateTimeOffset receivedAtUtc, long receivedAtStopwatchTicks, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class StateSink : IConnectionStateSink
    {
        public List<ConnectionStateChanged> Changes { get; } = [];
        public ValueTask PublishAsync(ConnectionStateChanged change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ImmediateClock : ILifecycleClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public long GetStopwatchTimestamp() => 1;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedRandom(double value) : IRandomSource
    {
        public double NextDouble() => value;
    }
}
