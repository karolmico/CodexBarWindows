using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class RateWindowTests
{
    [Fact]
    public void Keeps_the_provider_value_and_clamps_only_display()
    {
        var window = new RateWindow(usedPercent: 137.5);

        Assert.Equal(137.5, window.UsedPercent);
        Assert.Equal(100d, window.DisplayUsedPercent);
        Assert.Equal(0d, window.RemainingPercent);
        Assert.True(window.IsExhausted);
    }

    [Fact]
    public void Remaining_percent_is_complementary_and_clamped()
    {
        Assert.Equal(58d, new RateWindow(42d).RemainingPercent);
        Assert.Equal(100d, new RateWindow(-5d).RemainingPercent);
    }

    [Fact]
    public void Session_window_is_exhausted_at_exactly_one_hundred()
    {
        Assert.True(new RateWindow(100d).IsExhausted);
        Assert.False(new RateWindow(99.9).IsExhausted);
    }

    [Fact]
    public void Synthetic_placeholder_is_never_measured_or_exhausted()
    {
        var placeholder = new RateWindow(100d, isSyntheticPlaceholder: true);

        Assert.False(placeholder.IsMeasured);
        Assert.False(placeholder.IsExhausted);

        var snapshot = new UsageSnapshot(
            Providers.ProviderKeys.Claude,
            DateTimeOffset.UnixEpoch,
            primary: placeholder);

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Null(snapshot.HighestUsedPercent);
        Assert.False(snapshot.IsExhausted);
    }

    [Fact]
    public void Rejects_non_finite_percentages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateWindow(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateWindow(double.PositiveInfinity));
    }

    [Fact]
    public void Trims_blank_titles_to_absent()
    {
        Assert.Null(new RateWindow(1d, title: "  ").Title);
        Assert.Equal("Opus", new RateWindow(1d, title: " Opus ").Title);
    }
}
