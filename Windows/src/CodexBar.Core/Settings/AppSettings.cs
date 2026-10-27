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

    public AppSettings(
        IEnumerable<ProviderPreference>? providers = null,
        TimeSpan? refreshFloor = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        this.Providers = (providers ?? []).ToArray();
        this.RefreshFloor = ClampFloor(refreshFloor ?? TimeSpan.FromMinutes(MinRefreshFloorMinutes));
        this.SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }

    /// <summary>Provider order and enablement; unknown keys are preserved for forward compatibility.</summary>
    public IReadOnlyList<ProviderPreference> Providers { get; }

    /// <summary>Never poll faster than this, even when a window is nearly exhausted.</summary>
    public TimeSpan RefreshFloor { get; }

    public IReadOnlyList<ProviderKey> EnabledProviderKeys =>
        this.Providers.Where(static preference => preference.Enabled).Select(static preference => preference.Key).ToArray();

    /// <summary>Defaults derived from the registered providers, so a first launch needs no configuration.</summary>
    public static AppSettings DefaultFor(IEnumerable<ProviderDescriptor> providers) =>
        new(providers.Select(descriptor => new ProviderPreference(descriptor.Key, descriptor.EnabledByDefault)));

    private static TimeSpan ClampFloor(TimeSpan value)
    {
        var minutes = Math.Clamp(
            value.TotalMinutes,
            MinRefreshFloorMinutes,
            MaxRefreshFloorMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}
