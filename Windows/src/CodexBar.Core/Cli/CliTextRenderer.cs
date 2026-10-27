using System.Globalization;
using CodexBar.Core.Formatting;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Cli;

/// <summary>
/// Plain-text output that mirrors the macOS CLI summary: a <c>== Provider (source) ==</c> header per
/// provider, one bar line per measured window, then reset, account, and plan lines.
/// </summary>
public static class CliTextRenderer
{
    private const int BarWidth = 12;

    public static string Render(
        IReadOnlyList<CliProviderResult> results,
        DateTimeOffset now,
        string? version = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        var sections = results
            .Select(result => RenderProvider(result, now, version))
            .Where(static section => section.Length > 0)
            .ToArray();

        return string.Join("\n\n", sections);
    }

    /// <summary>Twelve-cell remaining-usage bar, e.g. <c>[========----]</c>.</summary>
    public static string Bar(double usedPercent)
    {
        var remaining = Math.Clamp(100d - usedPercent, 0d, 100d);
        var filled = Math.Clamp((int)(remaining / 100d * BarWidth), 0, BarWidth);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{new string('=', filled)}{new string('-', BarWidth - filled)}]");
    }

    /// <summary>Upstream reports remaining quota, not usage, and keeps a near-full lane at <c>&lt;1% left</c>.</summary>
    public static string RemainingText(double usedPercent)
    {
        var remaining = Math.Clamp(100d - usedPercent, 0d, 100d);
        if (remaining > 0d && remaining < 1d)
        {
            return "<1% left";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Round(remaining, MidpointRounding.AwayFromZero):0}% left");
    }

    private static string RenderProvider(CliProviderResult result, DateTimeOffset now, string? version)
    {
        var header = string.IsNullOrWhiteSpace(version)
            ? $"{result.DisplayName} ({result.Source})"
            : $"{result.DisplayName} {version} ({result.Source})";

        var lines = new List<string> { $"== {header} ==" };

        if (result.Error is { } error)
        {
            lines.Add($"Error: {error.Message}");
            return string.Join("\n", lines);
        }

        if (result.Snapshot is not { } snapshot)
        {
            return string.Join("\n", lines);
        }

        AppendWindow(lines, "Session", snapshot.Primary, now);
        AppendWindow(lines, "Weekly", snapshot.Secondary, now);

        foreach (var extra in snapshot.ExtraWindows)
        {
            if (extra.Window.IsDisabled)
            {
                lines.Add($"{extra.Title}: Disabled");
                if (extra.Window.DisabledReason is { } reason)
                {
                    lines.Add(reason);
                }

                continue;
            }

            if (!extra.UsageKnown)
            {
                lines.Add($"{extra.Title}: Unavailable");
                continue;
            }

            AppendWindow(lines, extra.Title, extra.Window, now);
        }

        if (snapshot.Identity is { } identity)
        {
            if (identity.AccountLabel is { } account)
            {
                lines.Add($"Account: {account}");
            }

            if (identity.PlanName is { } plan)
            {
                lines.Add($"Plan: {plan}");
            }

            if (identity.Organization is { } organization)
            {
                lines.Add($"Organization: {organization}");
            }
        }

        return string.Join("\n", lines);
    }

    private static void AppendWindow(List<string> lines, string title, RateWindow? window, DateTimeOffset now)
    {
        // Synthetic placeholders stand in for a lane the provider never reported; printing them as 0 %
        // would invent capacity, so they stay out of the summary exactly like upstream's `measured` filter.
        if (window is null || !window.IsMeasured)
        {
            return;
        }

        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{title}: {RemainingText(window.UsedPercent)} {Bar(window.UsedPercent)}"));
        AppendReset(lines, window, now);
    }

    private static void AppendReset(List<string> lines, RateWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is { } resetsAt)
        {
            var countdown = UsageFormatter.FormatCountdown(resetsAt - now);
            lines.Add(countdown == "now"
                ? "Resets now"
                : string.Create(CultureInfo.InvariantCulture, $"Resets in {countdown}"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(window.ResetDescription))
        {
            lines.Add(window.ResetDescription.Trim());
        }
    }
}
