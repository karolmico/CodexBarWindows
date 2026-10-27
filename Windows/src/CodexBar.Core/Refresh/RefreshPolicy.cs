using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Refresh;

/// <summary>Everything the refresh policy needs to decide the next fetch time.</summary>
public sealed record RefreshState
{
    public RefreshState(
        DateTimeOffset now,
        UsageSnapshot? snapshot = null,
        ProviderError? lastError = null,
        int consecutiveFailures = 0)
    {
        if (consecutiveFailures < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveFailures),
                consecutiveFailures,
                "Failure count cannot be negative.");
        }

        this.Now = now;
        this.Snapshot = snapshot;
        this.LastError = lastError;
        this.ConsecutiveFailures = consecutiveFailures;
    }

    public DateTimeOffset Now { get; }

    public UsageSnapshot? Snapshot { get; }

    public ProviderError? LastError { get; }

    public int ConsecutiveFailures { get; }
}

/// <summary>
/// Pure scheduling decision: how long to wait before the next fetch. Kept free of timers and
/// clocks so the matrix can be tested directly.
/// </summary>
public sealed class RefreshPolicy
{
    public TimeSpan IdleInterval { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ElevatedInterval { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan CriticalInterval { get; init; } = TimeSpan.FromMinutes(1);

    public double ElevatedThreshold { get; init; } = 80d;

    public double CriticalThreshold { get; init; } = 95d;

    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Snapshots older than this are treated as absent (sleep, hibernation, long offline).</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan NextDelay(RefreshState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.LastError is not null)
        {
            var failures = state.ConsecutiveFailures > 0 ? state.ConsecutiveFailures : 1;
            return this.Backoff(failures);
        }

        if (state.Snapshot is null || state.Now - state.Snapshot.UpdatedAt > this.StaleAfter)
        {
            return TimeSpan.Zero;
        }

        var highest = state.Snapshot.HighestUsedPercent;
        if (highest is null)
        {
            return this.IdleInterval;
        }

        if (highest >= this.CriticalThreshold)
        {
            return this.CriticalInterval;
        }

        if (highest >= this.ElevatedThreshold)
        {
            return this.ElevatedInterval;
        }

        return this.IdleInterval;
    }

    public TimeSpan Backoff(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        // 30s, 60s, 120s, ... capped at BackoffMax. Computed with a shift guard so a long
        // outage cannot overflow the exponent.
        var exponent = Math.Min(consecutiveFailures - 1, 20);
        var seconds = this.BackoffBase.TotalSeconds * Math.Pow(2, exponent);
        var capped = Math.Min(seconds, this.BackoffMax.TotalSeconds);
        return TimeSpan.FromSeconds(capped);
    }
}
