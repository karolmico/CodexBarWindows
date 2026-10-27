using System.Globalization;
using CodexBar.Core.Formatting;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Presentation;

/// <summary>Health of one provider, used for the dot on its flyout tab.</summary>
public enum FlyoutStatusKind
{
    /// <summary>Fresh data and no error.</summary>
    Ok,

    /// <summary>Usable but degraded: cached data behind an error, or a provider reporting no quota.</summary>
    Warning,

    /// <summary>No data at all because the last fetch failed.</summary>
    Error,

    /// <summary>Nothing fetched yet.</summary>
    Unknown,
}

/// <summary>One quota bar in the selected provider's detail pane.</summary>
public sealed record FlyoutWindowModel(
    string Title,
    double UsedPercent,
    string UsageText,
    string ResetText,
    bool IsExhausted,
    string? PaceText,
    string Accent);

/// <summary>One provider: its tab in the switcher and the detail pane shown when it is selected.</summary>
public sealed record FlyoutProviderModel(
    string Key,
    string Name,
    string IconPath,
    string Accent,
    string? Plan,
    string? Account,
    IReadOnlyList<FlyoutWindowModel> Windows,
    string? Status,
    FlyoutStatusKind StatusKind,
    string UpdatedText,
    ProviderLinkTarget Links,
    bool IsHighlighted)
{
    /// <summary>Set by <see cref="FlyoutViewModel"/>; a record so a tab repaint is one new value.</summary>
    public bool IsSelected { get; init; }

    /// <summary>Account on one line under the title; the plan already shows as a badge.</summary>
    public string Subtitle =>
        string.Join(" · ", new[] { this.Account }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>Label for the provider's setup action; a failed provider is asked to add its account.</summary>
    public string SetupLabel => this.StatusKind == FlyoutStatusKind.Error ? "Add account…" : "Configure…";

    public bool HasPace => this.Windows.Any(static window => window.PaceText is not null);

    public bool HasLinks => this.Links.HasDashboard || this.Links.HasStatusPage;
}

/// <summary>Everything the flyout renders, derived only from provider state and a clock.</summary>
public sealed record FlyoutModel(
    IReadOnlyList<FlyoutProviderModel> Providers,
    string FooterText,
    bool IsRefreshing,
    bool HasData);

/// <summary>Pure projection of provider state into flyout tabs and detail panes.</summary>
public static class FlyoutBuilder
{
    /// <summary>Below this the copy reads "just now" instead of a countdown.</summary>
    private static readonly TimeSpan JustNow = TimeSpan.FromSeconds(75);

    /// <summary>A pace line needs the window to be meaningfully underway before it means anything.</summary>
    private const double MinElapsedFraction = 0.05;

    private const double MaxElapsedFraction = 0.95;

    /// <summary>Pace within this many points of the elapsed share counts as on track.</summary>
    private const double PaceTolerance = 5d;

    public static FlyoutModel Build(
        IReadOnlyList<ProviderState> providers,
        DateTimeOffset now,
        bool isRefreshing,
        string? highlightedKey = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var cards = providers
            .Select(provider => BuildCard(provider, now, string.Equals(
                provider.Key.Value,
                highlightedKey,
                StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var newest = providers
            .Select(static provider => provider.Snapshot?.UpdatedAt)
            .OfType<DateTimeOffset>()
            .DefaultIfEmpty()
            .Max();

        var hasData = providers.Any(static provider => provider.Snapshot is not null);
        var footer = isRefreshing
            ? "Refreshing…"
            : !hasData
                ? "No usage data yet"
                : $"Updated {UsageFormatter.FormatCountdown(now - newest)} ago";

        return new FlyoutModel(cards, footer, isRefreshing, hasData);
    }

    /// <summary>
    /// Compares usage with the share of the window that has already elapsed. Returns null when the
    /// window cannot be paced: no duration, no reset instant, or a window that has only just started
    /// or is about to end, where a sliding estimate would produce a misleading line.
    /// </summary>
    public static string? DescribePace(RateWindow? window, DateTimeOffset now)
    {
        if (window is null || !window.IsMeasured
            || window.Window is not { } duration || duration <= TimeSpan.Zero
            || window.ResetsAt is not { } resetsAt)
        {
            return null;
        }

        var elapsed = duration - (resetsAt - now);
        var fraction = elapsed / duration;
        if (fraction < MinElapsedFraction || fraction > MaxElapsedFraction)
        {
            return null;
        }

        var delta = Math.Round(window.UsedPercent - (fraction * 100d), MidpointRounding.AwayFromZero);
        if (delta >= PaceTolerance)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Pace: ahead (+{delta:0}%) · runs out early");
        }

        return delta <= -PaceTolerance
            ? string.Create(CultureInfo.InvariantCulture, $"Pace: behind ({delta:0}%) · lasts to reset")
            : "Pace: on track · lasts to reset";
    }

    private static FlyoutProviderModel BuildCard(ProviderState provider, DateTimeOffset now, bool isHighlighted)
    {
        var visual = ProviderVisuals.For(provider.Key);
        var windows = new List<FlyoutWindowModel>();
        if (provider.Snapshot is { } snapshot)
        {
            AddWindow(windows, "Session", snapshot.Primary, now, visual.Accent);
            AddWindow(windows, "Weekly", snapshot.Secondary, now, visual.Accent);
            foreach (var extra in snapshot.ExtraWindows)
            {
                if (extra.UsageKnown || extra.Window.ResetsAt is not null
                    || !string.IsNullOrWhiteSpace(extra.Window.ResetDescription))
                {
                    AddWindow(windows, extra.Title, extra.Window, now, visual.Accent);
                }
            }
        }

        var status = provider.Error is not null
            ? ProviderStatusText.Label(provider.Error)
            : windows.Count == 0
                ? "No quota reported"
                : null;

        return new FlyoutProviderModel(
            provider.Key.Value,
            provider.DisplayName,
            visual.IconPath,
            visual.Accent,
            provider.Snapshot?.Identity?.PlanName,
            provider.Snapshot?.Identity?.AccountLabel,
            windows,
            status,
            StatusKind(provider, windows.Count),
            UpdatedText(provider, now),
            ProviderLinks.For(provider.Key),
            isHighlighted);
    }

    private static FlyoutStatusKind StatusKind(ProviderState provider, int windowCount) =>
        (provider.Error, provider.Snapshot) switch
        {
            (not null, null) => FlyoutStatusKind.Error,
            (not null, _) => FlyoutStatusKind.Warning,
            (null, null) => FlyoutStatusKind.Unknown,
            (null, _) => windowCount == 0 ? FlyoutStatusKind.Warning : FlyoutStatusKind.Ok,
        };

    private static string UpdatedText(ProviderState provider, DateTimeOffset now)
    {
        if (provider.Snapshot is not { } snapshot)
        {
            return provider.Error is null ? "Not fetched yet" : "Not available";
        }

        var age = now - snapshot.UpdatedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age < JustNow
            ? "Updated just now"
            : $"Updated {UsageFormatter.FormatCountdown(age)} ago";
    }

    private static void AddWindow(
        ICollection<FlyoutWindowModel> windows,
        string title,
        RateWindow? window,
        DateTimeOffset now,
        string accent)
    {
        if (window is null || !window.IsMeasured)
        {
            return;
        }

        windows.Add(new FlyoutWindowModel(
            title,
            Math.Clamp(window.UsedPercent, 0d, 100d),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{UsageFormatter.FormatPercent(window.UsedPercent)} used"),
            Capitalize(UsageFormatter.FormatReset(window, now)),
            window.IsExhausted,
            DescribePace(window, now),
            accent));
    }

    /// <summary>The detail pane starts a line with the reset copy, so it gets a capital letter.</summary>
    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
