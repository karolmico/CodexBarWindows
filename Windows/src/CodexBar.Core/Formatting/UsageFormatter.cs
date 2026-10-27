using System.Globalization;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Formatting;

/// <summary>Presentation of usage values for the tray, flyout, and CLI. Pure functions.</summary>
public static class UsageFormatter
{
    /// <summary>Rounded whole percent, clamped for display; the raw value stays on the window.</summary>
    public static string FormatPercent(double usedPercent)
    {
        if (!double.IsFinite(usedPercent))
        {
            return "—";
        }

        var clamped = Math.Clamp(usedPercent, 0d, 100d);
        var rounded = Math.Round(clamped, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{rounded:0}%");
    }

    /// <summary>Compact countdown: <c>2d 4h</c>, <c>3h 12m</c>, <c>12m</c>, <c>&lt;1m</c>, <c>now</c>.</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining < TimeSpan.FromMinutes(1))
        {
            return "<1m";
        }

        var totalMinutes = (long)Math.Floor(remaining.TotalMinutes);
        if (totalMinutes < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{totalMinutes}m");
        }

        if (totalMinutes < 60 * 24)
        {
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours}h")
                : string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m");
        }

        var days = totalMinutes / (60 * 24);
        var remainingHours = totalMinutes % (60 * 24) / 60;
        return remainingHours == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{days}d")
            : string.Create(CultureInfo.InvariantCulture, $"{days}d {remainingHours}h");
    }

    /// <summary>Reset copy for one window, accounting for a textual reset description.</summary>
    public static string FormatReset(RateWindow? window, DateTimeOffset now)
    {
        if (window is null)
        {
            return "No reset reported";
        }

        if (window.ResetsAt is { } resetsAt)
        {
            var countdown = FormatCountdown(resetsAt - now);
            return countdown == "now"
                ? "resets now"
                : string.Create(CultureInfo.InvariantCulture, $"resets in {countdown}");
        }

        if (!string.IsNullOrWhiteSpace(window.ResetDescription))
        {
            return window.ResetDescription.Trim();
        }

        return "Reset time unknown";
    }

    /// <summary>One-line summary used by provider rows, e.g. <c>42% · resets in 3h 12m</c>.</summary>
    public static string FormatWindowLine(RateWindow? window, DateTimeOffset now)
    {
        if (window is null)
        {
            return "Not reported";
        }

        if (!window.IsMeasured)
        {
            return "Not measured";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatPercent(window.UsedPercent)} · {FormatReset(window, now)}");
    }
}
