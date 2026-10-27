using CodexBar.Core.Providers;

namespace CodexBar.Core.Settings;

/// <summary>One provider entry in the user's ordering and enablement list.</summary>
public sealed record ProviderPreference
{
    public ProviderPreference(ProviderKey key, bool enabled = true)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
        this.Enabled = enabled;
    }

    public ProviderKey Key { get; }

    public bool Enabled { get; }
}

/// <summary>User configuration persisted under the app data directory.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinRefreshFloorMinutes = 1;
    public const int MaxRefreshFloorMinutes = 60;
    public static readonly IReadOnlyList<int> DefaultNotificationThresholds = [80, 95];

    public AppSettings(
        IEnumerable<ProviderPreference>? providers = null,
        TimeSpan? refreshFloor = null,
        int schemaVersion = CurrentSchemaVersion,
        IEnumerable<int>? notificationThresholds = null,
        bool notifyResets = true,
        TimeOnly? quietHoursStart = null,
        TimeOnly? quietHoursEnd = null,
        bool startWithWindows = false)
    {
        this.Providers = (providers ?? []).ToArray();
        this.RefreshFloor = ClampFloor(refreshFloor ?? TimeSpan.FromMinutes(MinRefreshFloorMinutes));
        this.SchemaVersion = schemaVersion;
        this.NotificationThresholds = NormalizeThresholds(notificationThresholds);
        this.NotifyResets = notifyResets;
        this.QuietHoursStart = quietHoursStart;
        this.QuietHoursEnd = quietHoursEnd;
        this.StartWithWindows = startWithWindows;
    }

    public int SchemaVersion { get; }

    /// <summary>Provider order and enablement; unknown keys are preserved for forward compatibility.</summary>
    public IReadOnlyList<ProviderPreference> Providers { get; }

    /// <summary>Never poll faster than this, even when a window is nearly exhausted.</summary>
    public TimeSpan RefreshFloor { get; }

    /// <summary>Sorted usage percentages that raise one notification each per window cycle.</summary>
    public IReadOnlyList<int> NotificationThresholds { get; }

    /// <summary>Whether a window crossing the reset instant raises a notification.</summary>
    public bool NotifyResets { get; }

    public TimeOnly? QuietHoursStart { get; }

    public TimeOnly? QuietHoursEnd { get; }

    /// <summary>Opt-in per-user Run entry so CodexBar starts with Windows.</summary>
    public bool StartWithWindows { get; }

    /// <summary>Quiet hours need both ends; identical ends mean "always quiet" is treated as disabled.</summary>
    public bool HasQuietHours =>
        this.QuietHoursStart is { } start && this.QuietHoursEnd is { } end && start != end;

    public IReadOnlyList<ProviderKey> EnabledProviderKeys =>
        this.Providers.Where(static preference => preference.Enabled).Select(static preference => preference.Key).ToArray();

    /// <summary>Defaults derived from the registered providers, so a first launch needs no configuration.</summary>
    public static AppSettings DefaultFor(IEnumerable<ProviderDescriptor> providers) =>
        new(providers.Select(descriptor => new ProviderPreference(descriptor.Key, descriptor.EnabledByDefault)));

    private static IReadOnlyList<int> NormalizeThresholds(IEnumerable<int>? thresholds)
    {
        var normalized = (thresholds ?? DefaultNotificationThresholds)
            .Where(static threshold => threshold is > 0 and <= 100)
            .Distinct()
            .Order()
            .ToArray();
        return normalized;
    }

    private static TimeSpan ClampFloor(TimeSpan value)
    {
        var minutes = Math.Clamp(
            value.TotalMinutes,
            MinRefreshFloorMinutes,
            MaxRefreshFloorMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}
