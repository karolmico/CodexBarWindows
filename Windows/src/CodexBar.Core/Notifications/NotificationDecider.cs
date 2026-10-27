using System.Globalization;
using CodexBar.Core.Formatting;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Settings;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Notifications;

public enum UsageNotificationKind
{
    Threshold,
    Reset,
}

/// <summary>One user-facing notification decision. Copy lives here so it can be tested without a toast API.</summary>
public sealed record UsageNotification(
    ProviderKey Provider,
    string ProviderName,
    string WindowTitle,
    UsageNotificationKind Kind,
    double UsedPercent,
    int? Threshold,
    DateTimeOffset? ResetsAt)
{
    public bool IsWarning => this.Kind == UsageNotificationKind.Threshold;

    public string Title => this.Kind == UsageNotificationKind.Threshold
        ? string.Create(CultureInfo.InvariantCulture, $"{this.ProviderName} at {this.Threshold}%")
        : $"{this.ProviderName} reset";

    public string Message => this.Kind == UsageNotificationKind.Threshold
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{this.WindowTitle} is {UsageFormatter.FormatPercent(this.UsedPercent)} used.")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{this.WindowTitle} reset: {UsageFormatter.FormatPercent(this.UsedPercent)} used.");
}

/// <summary>
/// Decides which threshold and reset notifications to raise. State is per provider and quota lane:
/// each configured threshold fires once until usage drops below it, and each reset instant fires once.
/// Quiet hours and disabled categories suppress delivery without repeating the alert later.
/// </summary>
public sealed class NotificationDecider
{
    private static readonly TimeSpan ResetTolerance = TimeSpan.FromMinutes(1);

    private readonly Dictionary<LaneKey, LaneState> lanes = [];

    public IReadOnlyList<UsageNotification> Evaluate(
        IReadOnlyList<ProviderState> providers,
        AppSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(settings);

        var quiet = IsQuietHours(now, settings.QuietHoursStart, settings.QuietHoursEnd);
        var notifications = new List<UsageNotification>();

        foreach (var provider in providers)
        {
            // A failed provider has no fresh measurement; never notify from stale or missing data.
            if (provider.Error is not null || provider.Snapshot is not { } snapshot)
            {
                continue;
            }

            this.EvaluateLane(
                provider,
                "session",
                "Session",
                snapshot.Primary,
                settings,
                now,
                quiet,
                notifications);
            this.EvaluateLane(
                provider,
                "weekly",
                "Weekly",
                snapshot.Secondary,
                settings,
                now,
                quiet,
                notifications);

            foreach (var extra in snapshot.ExtraWindows)
            {
                if (!extra.UsageKnown)
                {
                    continue;
                }

                this.EvaluateLane(
                    provider,
                    extra.Id,
                    extra.Title,
                    extra.Window,
                    settings,
                    now,
                    quiet,
                    notifications);
            }
        }

        return notifications;
    }

    /// <summary>Quiet hours wrap past midnight when the start is later than the end.</summary>
    public static bool IsQuietHours(DateTimeOffset now, TimeOnly? start, TimeOnly? end)
    {
        if (start is not { } from || end is not { } to || from == to)
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(now.DateTime);
        return from < to
            ? time >= from && time < to
            : time >= from || time < to;
    }

    private void EvaluateLane(
        ProviderState provider,
        string laneId,
        string title,
        RateWindow? window,
        AppSettings settings,
        DateTimeOffset now,
        bool quiet,
        List<UsageNotification> notifications)
    {
        if (window is null || !window.IsMeasured)
        {
            return;
        }

        var key = new LaneKey(provider.Key, laneId);
        if (!this.lanes.TryGetValue(key, out var state))
        {
            state = new LaneState();
            this.lanes[key] = state;
        }

        var resetDetected = this.DetectReset(provider, title, window, settings, quiet, notifications, state);
        var thresholds = settings.NotificationThresholds;
        var crossed = thresholds
            .Where(threshold => window.UsedPercent >= threshold)
            .DefaultIfEmpty(0)
            .Max();

        if (resetDetected)
        {
            // A window that just reset starts a new cycle; do not stack a threshold toast on top of it.
            state.NotifiedThreshold = crossed;
            return;
        }

        if (thresholds.Count == 0)
        {
            state.NotifiedThreshold = 0;
            return;
        }

        if (crossed > state.NotifiedThreshold)
        {
            state.NotifiedThreshold = crossed;
            if (!quiet)
            {
                notifications.Add(new UsageNotification(
                    provider.Key,
                    provider.DisplayName,
                    title,
                    UsageNotificationKind.Threshold,
                    window.UsedPercent,
                    crossed,
                    window.ResetsAt));
            }
        }
        else if (crossed == 0)
        {
            state.NotifiedThreshold = 0;
        }
    }

    private bool DetectReset(
        ProviderState provider,
        string title,
        RateWindow window,
        AppSettings settings,
        bool quiet,
        List<UsageNotification> notifications,
        LaneState state)
    {
        if (window.ResetsAt is not { } resetsAt)
        {
            return false;
        }

        var resetDetected = state.ResetsAt is { } previous
            && resetsAt - previous > ResetTolerance;
        state.ResetsAt = resetsAt;

        if (!resetDetected)
        {
            return false;
        }

        state.NotifiedThreshold = 0;
        if (settings.NotifyResets && !quiet)
        {
            notifications.Add(new UsageNotification(
                provider.Key,
                provider.DisplayName,
                title,
                UsageNotificationKind.Reset,
                window.UsedPercent,
                Threshold: null,
                resetsAt));
        }

        return true;
    }

    private readonly record struct LaneKey(ProviderKey Provider, string Lane);

    private sealed class LaneState
    {
        public int NotifiedThreshold { get; set; }

        public DateTimeOffset? ResetsAt { get; set; }
    }
}
