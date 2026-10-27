using System.Globalization;
using CodexBar.Core.Formatting;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Presentation;

/// <summary>One quota bar in a flyout provider card.</summary>
public sealed record FlyoutWindowModel(
    string Title,
    double UsedPercent,
    string UsageText,
    string ResetText,
    bool IsExhausted);

/// <summary>One provider card in the flyout.</summary>
public sealed record FlyoutProviderModel(
    string Key,
    string Name,
    string? Plan,
    string? Account,
    IReadOnlyList<FlyoutWindowModel> Windows,
    string? Status,
    bool IsError,
    bool IsHighlighted)
{
    /// <summary>Plan and account on one line; either may be absent.</summary>
    public string Subtitle =>
        string.Join(" · ", new[] { this.Plan, this.Account }.Where(static part => !string.IsNullOrWhiteSpace(part)));
}

/// <summary>Everything the flyout renders, derived only from provider state and a clock.</summary>
public sealed record FlyoutModel(
    IReadOnlyList<FlyoutProviderModel> Providers,
    string FooterText,
    bool IsRefreshing,
    bool HasData);

/// <summary>Pure projection of provider state into flyout cards.</summary>
public static class FlyoutBuilder
{
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

    private static FlyoutProviderModel BuildCard(ProviderState provider, DateTimeOffset now, bool isHighlighted)
    {
        var windows = new List<FlyoutWindowModel>();
        if (provider.Snapshot is { } snapshot)
        {
            AddWindow(windows, "Session", snapshot.Primary, now);
            AddWindow(windows, "Weekly", snapshot.Secondary, now);
            foreach (var extra in snapshot.ExtraWindows)
            {
                if (extra.UsageKnown || extra.Window.ResetsAt is not null
                    || !string.IsNullOrWhiteSpace(extra.Window.ResetDescription))
                {
                    AddWindow(windows, extra.Title, extra.Window, now);
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
            provider.Snapshot?.Identity?.PlanName,
            provider.Snapshot?.Identity?.AccountLabel,
            windows,
            status,
            provider.Error is not null,
            isHighlighted);
    }

    private static void AddWindow(
        ICollection<FlyoutWindowModel> windows,
        string title,
        RateWindow? window,
        DateTimeOffset now)
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
            UsageFormatter.FormatReset(window, now),
            window.IsExhausted));
    }
}
