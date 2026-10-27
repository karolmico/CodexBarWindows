using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Tests.Support;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class TrayMenuBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_registry_shows_an_honest_empty_state()
    {
        var model = TrayMenuBuilder.Build([], Now, isRefreshing: false);

        Assert.Equal("CodexBar — no providers enabled", model.ToolTip);
        Assert.False(model.HasData);
        Assert.Equal(
            ["empty", "separator:primary", "refresh", "separator:secondary", "exit"],
            model.Entries.Select(entry => entry.Id));
        Assert.False(model.Entries.Single(entry => entry.Id == TrayMenuBuilder.RefreshId).IsEnabled);
        Assert.Equal("No providers enabled", model.Entries[0].Label);
    }

    [Fact]
    public void Settings_entry_appears_only_when_settings_are_available()
    {
        var withoutSettings = TrayMenuBuilder.Build([], Now, isRefreshing: false);
        var withSettings = TrayMenuBuilder.Build([], Now, isRefreshing: false, settingsAvailable: true);

        Assert.DoesNotContain(withoutSettings.Entries, entry => entry.Id == TrayMenuBuilder.SettingsId);
        Assert.Contains(withSettings.Entries, entry => entry.Id == TrayMenuBuilder.SettingsId);
    }

    [Fact]
    public void Providers_are_listed_in_configured_order_with_usage_and_reset()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, 42d, 87d)),
            new ProviderState(ProviderKeys.Claude, "Claude", TestSnapshots.Claude(Now, 10d, 63d)),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("provider:codex", model.Entries[0].Id);
        Assert.Equal("Codex — 42% · resets in 3h", model.Entries[0].Label);
        Assert.Equal("Claude — 10% · resets in 1h", model.Entries[1].Label);
        Assert.True(model.HasData);
    }

    [Fact]
    public void Tooltip_summarises_up_to_three_providers_by_session_window()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, 42d, 87d)),
            new ProviderState(ProviderKeys.Claude, "Claude", TestSnapshots.Claude(Now, 10d, 63d)),
        };

        var toolTip = TrayMenuBuilder.BuildToolTip(providers, isRefreshing: false);

        Assert.Equal("CodexBar — Codex 42% · Claude 10%", toolTip);
    }

    [Fact]
    public void Tooltip_reports_refreshing_when_no_data_exists_yet()
    {
        var providers = new[] { new ProviderState(ProviderKeys.Codex, "Codex") };

        Assert.Equal("CodexBar — refreshing…", TrayMenuBuilder.BuildToolTip(providers, isRefreshing: true));
        Assert.Equal("CodexBar — no usage data", TrayMenuBuilder.BuildToolTip(providers, isRefreshing: false));
    }

    [Fact]
    public void Tooltip_truncates_to_the_windows_notification_area_limit()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex Enterprise Workspace", TestSnapshots.Codex(Now, 42d, 87d)),
            new ProviderState(ProviderKeys.Claude, "Claude Enterprise Workspace", TestSnapshots.Claude(Now, 10d, 63d)),
            new ProviderState(ProviderKeys.Gemini, "Gemini Enterprise Workspace", TestSnapshots.Claude(Now, 7d, 12d)),
        };

        var toolTip = TrayMenuBuilder.BuildToolTip(providers, isRefreshing: false);

        Assert.Equal(63, toolTip.Length);
        Assert.EndsWith("…", toolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_fetch_without_a_snapshot_explains_the_error()
    {
        var providers = new[]
        {
            new ProviderState(
                ProviderKeys.Claude,
                "Claude",
                error: ProviderError.AuthenticationRequired("run `claude` to sign in")),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("Claude — sign-in required", model.Entries[0].Label);
        Assert.False(model.HasData);
    }

    [Fact]
    public void Failed_refresh_keeps_the_last_snapshot_visible_and_marks_it_stale()
    {
        var providers = new[]
        {
            new ProviderState(
                ProviderKeys.Codex,
                "Codex",
                TestSnapshots.Codex(Now.AddMinutes(-4), 42d, 87d),
                new ProviderError(ProviderErrorKind.Network, "offline")),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("Codex — 42% · resets in 2h 56m (offline)", model.Entries[0].Label);
        Assert.True(model.HasData);
    }

    [Fact]
    public void Exhausted_provider_is_labelled()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, 100d, 20d)),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("Codex — 100% · resets in 3h · exhausted", model.Entries[0].Label);
    }

    [Fact]
    public void Refresh_entry_is_disabled_while_a_refresh_is_running()
    {
        var providers = new[] { new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, 1d, 2d)) };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: true);

        var refresh = model.Entries.Single(entry => entry.Id == TrayMenuBuilder.RefreshId);
        Assert.False(refresh.IsEnabled);
        Assert.Equal("Refreshing…", refresh.Label);
    }

    [Fact]
    public void Cached_snapshot_older_than_the_threshold_is_marked_with_its_age()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now.AddHours(-2), 42d, 87d)),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("Codex — 42% · resets in 1h · cached 2h ago", model.Entries[0].Label);
    }

    [Fact]
    public void Fresh_snapshot_is_not_marked_as_cached()
    {
        var providers = new[]
        {
            new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now.AddMinutes(-1), 42d, 87d)),
        };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.DoesNotContain("cached", model.Entries[0].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Weekly_only_snapshot_is_labelled_as_weekly()
    {
        var snapshot = new UsageSnapshot(ProviderKeys.Claude, Now, secondary: new RateWindow(63d, resetsAt: Now.AddDays(3)));
        var providers = new[] { new ProviderState(ProviderKeys.Claude, "Claude", snapshot) };

        var model = TrayMenuBuilder.Build(providers, Now, isRefreshing: false);

        Assert.Equal("Claude — 63% weekly · resets in 3d", model.Entries[0].Label);
    }
}
