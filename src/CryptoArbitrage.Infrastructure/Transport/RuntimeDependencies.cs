using System.Diagnostics;

namespace CryptoArbitrage.Infrastructure.Transport;

public sealed class SystemLifecycleClock : ILifecycleClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long GetStopwatchTimestamp() => Stopwatch.GetTimestamp();
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class SystemRandomSource : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}

public static class ReconnectDelayCalculator
{
    public static TimeSpan Calculate(LifecycleReconnectOptions options, int consecutiveFailures, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);
        if (consecutiveFailures <= 0) throw new ArgumentOutOfRangeException(nameof(consecutiveFailures));

        var baseMilliseconds = options.InitialDelay.TotalMilliseconds * Math.Pow(options.Multiplier, consecutiveFailures - 1);
        var cappedMilliseconds = Math.Min(baseMilliseconds, options.MaximumDelay.TotalMilliseconds);
        var jitterMultiplier = 1 + ((random.NextDouble() * 2 - 1) * options.JitterFraction);
        return TimeSpan.FromMilliseconds(Math.Min(cappedMilliseconds * jitterMultiplier, options.MaximumDelay.TotalMilliseconds));
    }
}
