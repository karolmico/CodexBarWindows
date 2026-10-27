using CodexBar.Core.Identity;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class UsageSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Highest_used_percent_spans_session_weekly_and_extra_windows()
    {
        var snapshot = TestSnapshots.Codex(
            Now,
            session: 42d,
            weekly: 87d,
            extra: [new NamedRateWindow("spark", "Codex Spark", new RateWindow(91d))]);

        Assert.Equal(91d, snapshot.HighestUsedPercent);
    }

    [Fact]
    public void Unknown_named_window_usage_does_not_count()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Claude,
            Now,
            primary: new RateWindow(10d),
            extraWindows: [new NamedRateWindow("opus", "Opus", new RateWindow(99d), usageKnown: false)]);

        Assert.Equal(10d, snapshot.HighestUsedPercent);
    }

    [Fact]
    public void Exhaustion_ignores_unmeasured_windows()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Codex,
            Now,
            primary: new RateWindow(10d),
            secondary: new RateWindow(100d, isSyntheticPlaceholder: true));

        Assert.False(snapshot.IsExhausted);
        Assert.True(new UsageSnapshot(ProviderKeys.Codex, Now, primary: new RateWindow(100d)).IsExhausted);
    }

    [Fact]
    public void Snapshot_without_windows_reports_no_measurement()
    {
        var snapshot = new UsageSnapshot(ProviderKeys.OpenCode, Now);

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Null(snapshot.HighestUsedPercent);
        Assert.Empty(snapshot.MeasuredWindows);
    }

    [Fact]
    public void Identity_and_provider_are_preserved()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Codex,
            Now,
            primary: new RateWindow(1d),
            identity: new ProviderIdentity(planName: " Plus ", accountLabel: "user@example.com"));

        Assert.Equal("codex", snapshot.Provider.Value);
        Assert.Equal("Plus", snapshot.Identity!.PlanName);
        Assert.Null(snapshot.Identity.Organization);
    }
}
