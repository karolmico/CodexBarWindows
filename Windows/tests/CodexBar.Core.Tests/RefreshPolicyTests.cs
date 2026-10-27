using CodexBar.Core.Providers;
using CodexBar.Core.Refresh;
using CodexBar.Core.Tests.Support;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class RefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly RefreshPolicy Policy = new();

    [Fact]
    public void Refreshes_immediately_without_a_snapshot()
    {
        Assert.Equal(TimeSpan.Zero, Policy.NextDelay(new RefreshState(Now)));
    }

    [Theory]
    [InlineData(0d, 5)]
    [InlineData(79.9, 5)]
    [InlineData(80d, 2)]
    [InlineData(94.9, 2)]
    [InlineData(95d, 1)]
    [InlineData(120d, 1)]
    public void Tightens_the_interval_as_usage_approaches_exhaustion(double usedPercent, int expectedMinutes)
    {
        var snapshot = TestSnapshots.Codex(Now.AddSeconds(-30), session: usedPercent, weekly: 0d);

        var delay = Policy.NextDelay(new RefreshState(Now, snapshot));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    [Fact]
    public void Uses_the_highest_window_across_the_snapshot()
    {
        var snapshot = TestSnapshots.Codex(Now.AddSeconds(-30), session: 10d, weekly: 96d);

        Assert.Equal(TimeSpan.FromMinutes(1), Policy.NextDelay(new RefreshState(Now, snapshot)));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 900)]
    [InlineData(40, 900)]
    public void Backs_off_exponentially_and_caps_at_fifteen_minutes(int failures, int expectedSeconds)
    {
        var error = new ProviderError(ProviderErrorKind.Network, "offline");
        var snapshot = TestSnapshots.Codex(Now.AddSeconds(-10), session: 10d);

        var delay = Policy.NextDelay(new RefreshState(Now, snapshot, error, failures));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void A_success_resets_the_interval()
    {
        var error = new ProviderError(ProviderErrorKind.Network, "offline");
        var snapshot = TestSnapshots.Codex(Now.AddSeconds(-10), session: 10d);

        var afterFailure = Policy.NextDelay(new RefreshState(Now, snapshot, error, consecutiveFailures: 4));
        var afterSuccess = Policy.NextDelay(new RefreshState(Now, snapshot, lastError: null, consecutiveFailures: 0));

        Assert.Equal(TimeSpan.FromMinutes(4), afterFailure);
        Assert.Equal(TimeSpan.FromMinutes(5), afterSuccess);
    }

    [Fact]
    public void An_error_without_a_failure_count_still_backs_off_once()
    {
        var error = new ProviderError(ProviderErrorKind.Network, "offline");
        var snapshot = TestSnapshots.Codex(Now.AddSeconds(-10), session: 10d);

        var delay = Policy.NextDelay(new RefreshState(Now, snapshot, error, consecutiveFailures: 0));

        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void Refreshes_immediately_when_the_snapshot_is_stale_after_sleep()
    {
        var snapshot = TestSnapshots.Codex(Now.AddMinutes(-31), session: 10d);

        Assert.Equal(TimeSpan.Zero, Policy.NextDelay(new RefreshState(Now, snapshot)));
    }

    [Fact]
    public void A_snapshot_at_the_staleness_boundary_is_still_current()
    {
        var snapshot = TestSnapshots.Codex(Now.AddMinutes(-30), session: 10d);

        Assert.Equal(TimeSpan.FromMinutes(5), Policy.NextDelay(new RefreshState(Now, snapshot)));
    }

    [Fact]
    public void A_snapshot_without_measured_windows_uses_the_idle_interval()
    {
        var snapshot = new UsageSnapshot(ProviderKeys.Codex, Now.AddSeconds(-5));

        Assert.Equal(TimeSpan.FromMinutes(5), Policy.NextDelay(new RefreshState(Now, snapshot)));
    }
}
