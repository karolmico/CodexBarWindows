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

/// <summary>One quota lane in the selected provider's detail pane.</summary>
public sealed record FlyoutWindowModel(
    string Title,
    double UsedPercent,
    string UsageText,
    string ResetText,
    bool IsExhausted,
    string? PaceText,
    string Accent,
    bool IsDisabled = false,
    string? NoteText = null)
{
    /// <summary>A disabled lane has no meaningful fill, so its bar is hidden instead of drawn at zero.</summary>
    public bool ShowBar => !this.IsDisabled;

    /// <summary>The model family this lane belongs to; null when the provider has no groups.</summary>
    public string? Group { get; init; }

    /// <summary>Family name on the first lane of a family, so the flyout renders one header per family.</summary>
    public string? GroupHeader { get; init; }
}

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
            AddWindow(
                windows,
                snapshot.Primary?.Title ?? "Session",
                snapshot.Primary,
                now,
                visual.Accent,
                snapshot.Primary?.Group);
            AddWindow(
                windows,
                snapshot.Secondary?.Title ?? "Weekly",
                snapshot.Secondary,
                now,
                visual.Accent,
                snapshot.Secondary?.Group);
            foreach (var extra in snapshot.ExtraWindows)
            {
                if (extra.UsageKnown || extra.Window.IsDisabled || extra.Window.ResetsAt is not null
                    || !string.IsNullOrWhiteSpace(extra.Window.ResetDescription))
                {
                    AddWindow(
                        windows,
                        extra.Window.Title ?? extra.Title,
                        extra.Window,
                        now,
                        visual.Accent,
                        extra.Window.Group);
                }
            }
        }

        var grouped = GroupByFamily(windows);

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
            grouped,
            status,
            StatusKind(provider, windows.Count),
            UpdatedText(provider, now),
            ProviderLinks.For(provider.Key),
            isHighlighted);
    }

    /// <summary>
    /// Keeps lanes in family order and stamps the family name onto the first lane of each family, so the
    /// flyout can render a header above it. Providers without groups pass through untouched.
    /// </summary>
    private static IReadOnlyList<FlyoutWindowModel> GroupByFamily(IReadOnlyList<FlyoutWindowModel> windows)
    {
        if (!windows.Any(static window => window.Group is not null))
        {
            return windows;
        }

        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var sequence = 0;
        foreach (var window in windows)
        {
            if (window.Group is { } group && !order.ContainsKey(group))
            {
                order[group] = sequence++;
            }
        }

        var ordered = windows
            .OrderBy(window => window.Group is { } group ? order[group] : -1)
            .ToArray();

        string? lastGroup = null;
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Group is { } group && !string.Equals(group, lastGroup, StringComparison.Ordinal))
            {
                ordered[index] = ordered[index] with { GroupHeader = group };
                lastGroup = group;
            }
        }

        return ordered;
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
        string accent,
        string? group = null)
    {
        if (window is null || (!window.IsMeasured && !window.IsDisabled))
        {
            return;
        }

        if (window.IsDisabled)
        {
            // The provider still reports the lane, but switched it off. Dropping it silently made the
            // flyout look like it had lost data, so it stays with the provider's own explanation.
            windows.Add(new FlyoutWindowModel(
                title,
                UsedPercent: 0d,
                UsageText: "Disabled",
                ResetText: window.ResetsAt is null
                    ? string.Empty
                    : Capitalize(UsageFormatter.FormatReset(window, now)),
                IsExhausted: false,
                PaceText: null,
                accent,
                IsDisabled: true,
                NoteText: Truncate(window.DisabledReason, 160))
            {
                Group = group,
            });
            return;
        }

        var used = window.UsedPercent;
        var remaining = Math.Clamp(100d - used, 0d, 100d);

        windows.Add(new FlyoutWindowModel(
            title,
            Math.Clamp(used, 0d, 100d),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{UsageFormatter.FormatPercent(used)} used · {UsageFormatter.FormatPercent(remaining)} left"),
            Capitalize(UsageFormatter.FormatReset(window, now)),
            window.IsExhausted,
            DescribePace(window, now),
            accent)
        {
            Group = group,
        });
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>The detail pane starts a line with the reset copy, so it gets a capital letter.</summary>
    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
