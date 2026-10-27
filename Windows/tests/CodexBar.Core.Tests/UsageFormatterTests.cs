using CodexBar.Core.Formatting;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class UsageFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0d, "0%")]
    [InlineData(41.6, "42%")]
    [InlineData(41.4, "41%")]
    [InlineData(41.5, "42%")]
    [InlineData(100d, "100%")]
    [InlineData(140d, "100%")]
    [InlineData(-3d, "0%")]
    public void Rounds_percentages_for_display(double value, string expected)
    {
        Assert.Equal(expected, UsageFormatter.FormatPercent(value));
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(-30, "now")]
    [InlineData(30, "<1m")]
    [InlineData(12 * 60, "12m")]
    [InlineData(60 * 60, "1h")]
    [InlineData((3 * 60 + 12) * 60, "3h 12m")]
    [InlineData((23 * 60 + 59) * 60, "23h 59m")]
    [InlineData(24 * 60 * 60, "1d")]
    [InlineData((2 * 24 + 4) * 60 * 60, "2d 4h")]
    public void Formats_countdowns_compactly(int seconds, string expected)
    {
        Assert.Equal(expected, UsageFormatter.FormatCountdown(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Formats_reset_from_an_absolute_instant()
    {
        var window = new RateWindow(42d, resetsAt: Now.AddHours(3).AddMinutes(12));

        Assert.Equal("resets in 3h 12m", UsageFormatter.FormatReset(window, Now));
    }

    [Fact]
    public void Formats_reset_from_a_past_instant_as_now()
    {
        var window = new RateWindow(42d, resetsAt: Now.AddMinutes(-2));

        Assert.Equal("resets now", UsageFormatter.FormatReset(window, Now));
    }

    [Fact]
    public void Falls_back_to_a_textual_reset_description()
    {
        var window = new RateWindow(42d, resetDescription: "Resets on 3 Nov");

        Assert.Equal("Resets on 3 Nov", UsageFormatter.FormatReset(window, Now));
    }

    [Fact]
    public void Reports_unknown_reset_when_nothing_is_supplied()
    {
        Assert.Equal("Reset time unknown", UsageFormatter.FormatReset(new RateWindow(42d), Now));
        Assert.Equal("No reset reported", UsageFormatter.FormatReset(null, Now));
    }

    [Fact]
    public void Window_line_combines_percentage_and_reset()
    {
        var window = new RateWindow(87d, resetsAt: Now.AddDays(2).AddHours(4));

        Assert.Equal("87% · resets in 2d 4h", UsageFormatter.FormatWindowLine(window, Now));
    }

    [Fact]
    public void Window_line_marks_missing_and_unmeasured_windows()
    {
        Assert.Equal("Not reported", UsageFormatter.FormatWindowLine(null, Now));
        Assert.Equal("Not measured", UsageFormatter.FormatWindowLine(new RateWindow(0d, isSyntheticPlaceholder: true), Now));
    }
}
