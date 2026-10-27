using CodexBar.Core.Providers;
using CodexBar.Core.Settings;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class SettingsStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly AppSettings Defaults = new(
        [
            new ProviderPreference(ProviderKeys.Codex, enabled: true),
            new ProviderPreference(ProviderKeys.Claude, enabled: true),
            new ProviderPreference(ProviderKeys.Gemini, enabled: false),
        ],
        TimeSpan.FromMinutes(5));

    private static SettingsStore Store(
        Dictionary<string, string> files,
        out List<(string Path, string Content)> written,
        Func<DateTimeOffset>? clock = null)
    {
        var captured = new List<(string, string)>();
        written = captured;
        return new SettingsStore(
            @"C:\AppData\CodexBar\settings.json",
            path => files.TryGetValue(path, out var text) ? text : null,
            (path, content) => captured.Add((path, content)),
            clock ?? (() => Now));
    }

    [Fact]
    public void Missing_file_keeps_the_derived_defaults_without_a_warning()
    {
        var store = Store([], out var written);

        var result = store.Load(Defaults);

        Assert.Null(result.Warning);
        Assert.Equal(Defaults.Providers, result.Settings.Providers);
        Assert.Empty(written);
    }

    [Fact]
    public void Settings_round_trip_order_enablement_and_refresh_floor()
    {
        var files = new Dictionary<string, string>();
        var store = Store(files, out var written, () => Now);
        var settings = new AppSettings(
            [
                new ProviderPreference(ProviderKeys.Claude, enabled: false),
                new ProviderPreference(ProviderKeys.Codex, enabled: true),
            ],
            TimeSpan.FromMinutes(12));

        store.Save(settings);
        var (path, content) = written.Single();
        files[path] = content;
        var loaded = store.Load(Defaults).Settings;

        Assert.Equal(2, loaded.Providers.Count);
        Assert.Equal(ProviderKeys.Claude, loaded.Providers[0].Key);
        Assert.False(loaded.Providers[0].Enabled);
        Assert.Equal(ProviderKeys.Codex, loaded.Providers[1].Key);
        Assert.True(loaded.Providers[1].Enabled);
        Assert.Equal(TimeSpan.FromMinutes(12), loaded.RefreshFloor);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_are_reported()
    {
        var files = new Dictionary<string, string>
        {
            [@"C:\AppData\CodexBar\settings.json"] = "{ not json",
        };
        var store = Store(files, out var written);

        var result = store.Load(Defaults);

        Assert.Equal(Defaults.Providers, result.Settings.Providers);
        Assert.Contains("previous file was saved", result.Warning!, StringComparison.OrdinalIgnoreCase);
        var backup = written.Single();
        Assert.EndsWith(".corrupt-1793102400", backup.Path, StringComparison.Ordinal);
        Assert.Equal("{ not json", backup.Content);
    }

    [Fact]
    public void Newer_schema_is_left_untouched_and_defaults_are_used()
    {
        var files = new Dictionary<string, string>
        {
            [@"C:\AppData\CodexBar\settings.json"] = """{"schemaVersion":2,"providers":[{"key":"codex","enabled":false}]}""",
        };
        var store = Store(files, out var written);

        var result = store.Load(Defaults);

        Assert.Empty(written);
        Assert.Contains("newer than this build", result.Warning!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Defaults.Providers, result.Settings.Providers);
    }

    [Fact]
    public void Unknown_provider_keys_are_preserved_for_forward_compatibility()
    {
        var files = new Dictionary<string, string>
        {
            [@"C:\AppData\CodexBar\settings.json"] =
                """{"schemaVersion":1,"providers":[{"key":"future-provider","enabled":true},{"key":"codex","enabled":true}],"refreshFloorMinutes":7}""",
        };
        var store = Store(files, out _);

        var settings = store.Load(Defaults).Settings;

        Assert.Equal("future-provider", settings.Providers[0].Key.Value);
        Assert.Equal(TimeSpan.FromMinutes(7), settings.RefreshFloor);
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(0.2d, 1d)]
    [InlineData(12d, 12d)]
    [InlineData(999d, 60d)]
    public void Refresh_floor_is_clamped_to_a_sane_range(double minutes, double expected)
    {
        var settings = new AppSettings(refreshFloor: TimeSpan.FromMinutes(minutes));

        Assert.Equal(TimeSpan.FromMinutes(expected), settings.RefreshFloor);
    }

    [Fact]
    public void Notification_settings_round_trip()
    {
        var files = new Dictionary<string, string>();
        var store = Store(files, out var written, () => Now);
        var settings = new AppSettings(
            notificationThresholds: [70, 90],
            notifyResets: false,
            quietHoursStart: new TimeOnly(22, 30),
            quietHoursEnd: new TimeOnly(6, 15));

        store.Save(settings);
        var (path, content) = written.Single();
        files[path] = content;
        var loaded = store.Load(Defaults).Settings;

        Assert.Equal([70, 90], loaded.NotificationThresholds);
        Assert.False(loaded.NotifyResets);
        Assert.True(loaded.HasQuietHours);
        Assert.Equal(new TimeOnly(22, 30), loaded.QuietHoursStart);
        Assert.Equal(new TimeOnly(6, 15), loaded.QuietHoursEnd);
    }

    [Fact]
    public void Older_settings_file_keeps_notification_defaults()
    {
        var files = new Dictionary<string, string>
        {
            [@"C:\AppData\CodexBar\settings.json"] = """{"schemaVersion":1,"refreshFloorMinutes":4}""",
        };
        var store = Store(files, out _);

        var settings = store.Load(Defaults).Settings;

        Assert.Equal([80, 95], settings.NotificationThresholds);
        Assert.True(settings.NotifyResets);
        Assert.False(settings.HasQuietHours);
    }

    [Fact]
    public void Invalid_quiet_hours_are_ignored_instead_of_breaking_the_file()
    {
        var files = new Dictionary<string, string>
        {
            [@"C:\AppData\CodexBar\settings.json"] =
                """{"schemaVersion":1,"quietHoursStart":"25:99","quietHoursEnd":"07:00"}""",
        };
        var store = Store(files, out _);

        var settings = store.Load(Defaults).Settings;

        Assert.False(settings.HasQuietHours);
        Assert.Null(settings.QuietHoursStart);
    }

    [Fact]
    public void Thresholds_are_sorted_deduplicated_and_clamped()
    {
        var settings = new AppSettings(notificationThresholds: [95, 80, 95, 0, 120, 50]);

        Assert.Equal([50, 80, 95], settings.NotificationThresholds);
    }

    [Fact]
    public void Defaults_enable_providers_by_their_descriptor()
    {
        var defaults = AppSettings.DefaultFor(
        [
            new ProviderDescriptor(ProviderKeys.Codex, "Codex"),
            new ProviderDescriptor(ProviderKeys.Gemini, "Gemini", enabledByDefault: false),
        ]);

        var enabled = Assert.Single(defaults.EnabledProviderKeys);
        Assert.Equal(ProviderKeys.Codex, enabled);
    }
}
