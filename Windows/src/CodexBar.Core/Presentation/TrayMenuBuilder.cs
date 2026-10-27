using CodexBar.Core.Formatting;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Presentation;

/// <summary>What the tray knows about one provider: its last snapshot and, if the last fetch failed, the error.</summary>
public sealed record ProviderState
{
    public ProviderState(ProviderKey key, string displayName, UsageSnapshot? snapshot = null, ProviderError? error = null)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
        this.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("A provider state needs a display name.", nameof(displayName))
            : displayName.Trim();
        this.Snapshot = snapshot;
        this.Error = error;
    }

    public ProviderKey Key { get; }

    public string DisplayName { get; }

    /// <summary>Last good snapshot; may be stale when <see cref="Error"/> is set.</summary>
    public UsageSnapshot? Snapshot { get; }

    public ProviderError? Error { get; }
}

public sealed record TrayMenuEntry
{
    private TrayMenuEntry(string id, string label, bool isEnabled, bool isSeparator)
    {
        this.Id = id;
        this.Label = label;
        this.IsEnabled = isEnabled;
        this.IsSeparator = isSeparator;
    }

    public string Id { get; }

    public string Label { get; }

    public bool IsEnabled { get; }

    public bool IsSeparator { get; }

    public static TrayMenuEntry Item(string id, string label, bool isEnabled = true) =>
        new(id, label, isEnabled, isSeparator: false);

    public static TrayMenuEntry Separator(string id) => new(id, string.Empty, isEnabled: false, isSeparator: true);
}

public sealed record TrayMenuModel
{
    public TrayMenuModel(string toolTip, IReadOnlyList<TrayMenuEntry> entries, bool hasData)
    {
        this.ToolTip = toolTip;
        this.Entries = entries;
        this.HasData = hasData;
    }

    public string ToolTip { get; }

    public IReadOnlyList<TrayMenuEntry> Entries { get; }

    public bool HasData { get; }
}

/// <summary>Pure projection of provider state into the tray tooltip and context menu.</summary>
public static class TrayMenuBuilder
{
    public const string RefreshId = "refresh";
    public const string SettingsId = "settings";
    public const string ExitId = "exit";
    public const string EmptyId = "empty";

    /// <summary>Windows truncates notification-area tooltips beyond this length.</summary>
    private const int MaxToolTipLength = 63;

    public static TrayMenuModel Build(
        IReadOnlyList<ProviderState> providers,
        DateTimeOffset now,
        bool isRefreshing,
        bool settingsAvailable = false,
        TimeSpan? staleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var staleThreshold = staleAfter ?? TimeSpan.FromMinutes(30);
        var entries = new List<TrayMenuEntry>();
        var hasData = providers.Any(static provider => provider.Snapshot is not null);

        if (providers.Count == 0)
        {
            entries.Add(TrayMenuEntry.Item(EmptyId, "No providers enabled", isEnabled: false));
        }
        else
        {
            for (var index = 0; index < providers.Count; index++)
            {
                var provider = providers[index];
                entries.Add(TrayMenuEntry.Item(
                    $"provider:{provider.Key.Value}",
                    DescribeProvider(provider, now, staleThreshold)));
            }
        }

        entries.Add(TrayMenuEntry.Separator("separator:primary"));
        entries.Add(TrayMenuEntry.Item(
            RefreshId,
            isRefreshing ? "Refreshing…" : "Refresh",
            isEnabled: !isRefreshing && providers.Count > 0));
        if (settingsAvailable)
        {
            entries.Add(TrayMenuEntry.Item(SettingsId, "Settings…"));
        }

        entries.Add(TrayMenuEntry.Separator("separator:secondary"));
        entries.Add(TrayMenuEntry.Item(ExitId, "Exit"));

        return new TrayMenuModel(BuildToolTip(providers, isRefreshing), entries, hasData);
    }

    public static string BuildToolTip(IReadOnlyList<ProviderState> providers, bool isRefreshing)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Count == 0)
        {
            return "CodexBar — no providers enabled";
        }

        var summaries = providers
            .Where(static provider => provider.Snapshot is not null)
            .Take(3)
            .Select(provider => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{provider.DisplayName} {UsageFormatter.FormatPercent(SummaryPercent(provider.Snapshot!))}"))
            .ToList();

        if (summaries.Count == 0)
        {
            return isRefreshing ? "CodexBar — refreshing…" : "CodexBar — no usage data";
        }

        var toolTip = $"CodexBar — {string.Join(" · ", summaries)}";
        return toolTip.Length <= MaxToolTipLength
            ? toolTip
            : string.Concat(toolTip.AsSpan(0, MaxToolTipLength - 1), "…");
    }

    private static string DescribeProvider(ProviderState provider, DateTimeOffset now, TimeSpan staleThreshold)
    {
        if (provider.Snapshot is null)
        {
            return provider.Error is null
                ? $"{provider.DisplayName} — no data"
                : $"{provider.DisplayName} — {ErrorLabel(provider.Error)}";
        }

        var line = ProviderUsageLine(provider.Snapshot, now);
        var age = now - provider.Snapshot.UpdatedAt;
        if (age > staleThreshold)
        {
            line = $"{line} · cached {UsageFormatter.FormatCountdown(age)} ago";
        }

        return provider.Error is null
            ? $"{provider.DisplayName} — {line}"
            : $"{provider.DisplayName} — {line} ({ErrorLabel(provider.Error)})";
    }

    private static string ProviderUsageLine(UsageSnapshot snapshot, DateTimeOffset now)
    {
        RateWindow? window = snapshot.Primary ?? snapshot.Secondary
            ?? snapshot.ExtraWindows
                .Where(static named => named.UsageKnown)
                .Select(static named => named.Window)
                .OrderByDescending(static rateWindow => rateWindow.UsedPercent)
                .FirstOrDefault();

        if (window is null)
        {
            return "no quota reported";
        }

        var label = snapshot.Primary is not null
            ? string.Empty
            : snapshot.Secondary is not null
                ? " weekly"
                : string.Empty;
        var usage = $"{UsageFormatter.FormatPercent(window.UsedPercent)}{label} · {UsageFormatter.FormatReset(window, now)}";
        return window.IsExhausted ? $"{usage} · exhausted" : usage;
    }

    private static double SummaryPercent(UsageSnapshot snapshot)
    {
        if (snapshot.Primary is { IsMeasured: true } primary)
        {
            return primary.UsedPercent;
        }

        return snapshot.HighestUsedPercent ?? 0d;
    }

    private static string ErrorLabel(ProviderError error) => ProviderStatusText.Label(error);
}
