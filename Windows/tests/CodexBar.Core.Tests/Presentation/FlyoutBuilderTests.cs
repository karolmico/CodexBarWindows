using CodexBar.Core.Identity;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Tests.Support;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests.Presentation;

public sealed class FlyoutBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_primary_secondary_and_named_windows()
    {
        var snapshot = TestSnapshots.Codex(
            Now,
            session: 42,
            weekly: 61,
            extra: [new NamedRateWindow("flash-lite", "Flash Lite", new RateWindow(88, TimeSpan.FromDays(1), Now.AddHours(4)))]);

        var model = FlyoutBuilder.Build([new ProviderState(ProviderKeys.Codex, "Codex", snapshot)], Now, isRefreshing: false);

        var card = Assert.Single(model.Providers);
        Assert.Equal("Codex", card.Name);
        Assert.Equal("codex", card.Key);
        Assert.Equal("Plus", card.Plan);
        Assert.Null(card.Status);
        Assert.Equal(FlyoutStatusKind.Ok, card.StatusKind);
        Assert.Equal(["Session", "Weekly", "Flash Lite"], card.Windows.Select(window => window.Title));
        Assert.Equal(42d, card.Windows[0].UsedPercent);
        Assert.Equal("42% used", card.Windows[0].UsageText);
        Assert.StartsWith("Resets in", card.Windows[0].ResetText, StringComparison.Ordinal);
        Assert.False(card.IsHighlighted);
        Assert.Equal("Configure…", card.SetupLabel);
        Assert.StartsWith("Updated ", model.FooterText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tab_carries_a_mark_an_accent_and_a_freshness_line()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", TestSnapshots.Codex(Now))],
            Now,
            isRefreshing: false).Providers);

        Assert.False(string.IsNullOrWhiteSpace(card.IconPath));
        Assert.Matches("^#[0-9A-F]{6}$", card.Accent);
        Assert.Equal("Updated just now", card.UpdatedText);
    }

    [Fact]
    public void An_old_snapshot_counts_its_age()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now.AddHours(-2)))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal("Updated 2h ago", card.UpdatedText);
    }

    [Fact]
    public void Error_with_a_cached_snapshot_keeps_the_windows_and_warns()
    {
        var snapshot = TestSnapshots.Claude(Now.AddMinutes(-10), session: 90, weekly: 30);

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Claude, "Claude", snapshot, ProviderError.AuthenticationRequired("expired"))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal(2, card.Windows.Count);
        Assert.Equal(FlyoutStatusKind.Warning, card.StatusKind);
        Assert.Equal("sign-in required", card.Status);
        Assert.Equal("Configure…", card.SetupLabel);
    }

    [Fact]
    public void Provider_without_windows_explains_the_empty_state()
    {
        var snapshot = new UsageSnapshot(ProviderKeys.Copilot, Now);

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Copilot, "Copilot", snapshot)],
            Now,
            isRefreshing: false).Providers);

        Assert.Empty(card.Windows);
        Assert.Equal("No quota reported", card.Status);
        Assert.Equal(FlyoutStatusKind.Warning, card.StatusKind);
    }

    [Fact]
    public void Provider_without_a_snapshot_reports_the_error_label()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", error: ProviderError.NotConfigured("closed"))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal("not configured", card.Status);
        Assert.Equal(FlyoutStatusKind.Error, card.StatusKind);
        Assert.Equal("Add account…", card.SetupLabel);
        Assert.Equal("Not available", card.UpdatedText);
        Assert.Empty(card.Windows);
    }

    [Fact]
    public void Empty_registry_has_no_cards_and_a_honest_footer()
    {
        var model = FlyoutBuilder.Build([], Now, isRefreshing: false);

        Assert.Empty(model.Providers);
        Assert.False(model.HasData);
        Assert.Equal("No usage data yet", model.FooterText);
    }

    [Fact]
    public void Highlighted_provider_is_marked_for_the_toast_click_focus()
    {
        var snapshot = TestSnapshots.Codex(Now);

        var model = FlyoutBuilder.Build(
            [
                new ProviderState(ProviderKeys.Codex, "Codex", snapshot),
                new ProviderState(ProviderKeys.Claude, "Claude", TestSnapshots.Claude(Now)),
            ],
            Now,
            isRefreshing: false,
            highlightedKey: "claude");

        Assert.False(model.Providers[0].IsHighlighted);
        Assert.True(model.Providers[1].IsHighlighted);
    }

    [Fact]
    public void Refreshing_footer_wins_over_the_timestamp()
    {
        var snapshot = TestSnapshots.Codex(Now.AddMinutes(-2));

        var model = FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Codex, "Codex", snapshot)],
            Now,
            isRefreshing: true);

        Assert.Equal("Refreshing…", model.FooterText);
        Assert.True(model.HasData);
    }

    [Fact]
    public void Synthetic_placeholder_windows_are_not_rendered()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Cursor,
            Now,
            primary: new RateWindow(0, isSyntheticPlaceholder: true),
            identity: new ProviderIdentity(planName: "Cursor Pro"));

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Cursor, "Cursor", snapshot)],
            Now,
            isRefreshing: false).Providers);

        Assert.Empty(card.Windows);
        Assert.Equal("Cursor Pro", card.Plan);
    }

    [Fact]
    public void Pace_compares_usage_with_the_elapsed_share_of_the_window()
    {
        // Five-hour window with three hours left: 40% elapsed, so 42% used is on track.
        Assert.Equal(
            "Pace: on track · lasts to reset",
            FlyoutBuilder.DescribePace(new RateWindow(42, TimeSpan.FromHours(5), Now.AddHours(3)), Now));

        // Same window at 90% used runs hot.
        Assert.Equal(
            "Pace: ahead (+50%) · runs out early",
            FlyoutBuilder.DescribePace(new RateWindow(90, TimeSpan.FromHours(5), Now.AddHours(3)), Now));

        // Well under the elapsed share is comfortable.
        Assert.Equal(
            "Pace: behind (-30%) · lasts to reset",
            FlyoutBuilder.DescribePace(new RateWindow(10, TimeSpan.FromHours(5), Now.AddHours(3)), Now));
    }

    [Fact]
    public void Pace_stays_silent_when_the_window_cannot_be_paced()
    {
        var duration = TimeSpan.FromHours(5);

        Assert.Null(FlyoutBuilder.DescribePace(new RateWindow(42, resetsAt: Now.AddHours(3)), Now));
        Assert.Null(FlyoutBuilder.DescribePace(new RateWindow(42, duration), Now));
        Assert.Null(FlyoutBuilder.DescribePace(null, Now));

        // A window that has only just started, or is about to end, is a sliding estimate on some
        // providers; a pace line there would be noise.
        Assert.Null(FlyoutBuilder.DescribePace(new RateWindow(42, duration, Now.Add(duration)), Now));
        Assert.Null(FlyoutBuilder.DescribePace(new RateWindow(42, duration, Now.AddMinutes(5)), Now));
    }

    [Fact]
    public void Windows_are_paced_in_the_detail_pane()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, session: 42, weekly: 61))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal("Pace: on track · lasts to reset", card.Windows[0].PaceText);
        Assert.True(card.HasPace);
    }

    [Fact]
    public void Shipped_providers_have_a_dashboard_or_a_status_page()
    {
        foreach (var key in new[]
                 {
                     ProviderKeys.Codex, ProviderKeys.Claude, ProviderKeys.Copilot,
                     ProviderKeys.Cursor, ProviderKeys.Antigravity, ProviderKeys.OpenCode,
                 })
        {
            var links = ProviderLinks.For(key);
            Assert.True(links.HasDashboard || links.HasStatusPage, $"No links for {key.Value}");
            Assert.DoesNotContain(" ", links.DashboardUrl ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.True(ProviderLinks.For(ProviderKeys.Codex).HasDashboard);
        Assert.False(ProviderLinks.For(ProviderKeys.OpenCode).HasStatusPage);
        Assert.False(ProviderLinks.For(ProviderKey.Parse("retired-provider")).HasDashboard);
    }

    [Fact]
    public void Every_shipped_provider_gets_a_distinct_mark()
    {
        var visuals = new[]
            {
                ProviderKeys.Codex, ProviderKeys.Claude, ProviderKeys.Copilot,
                ProviderKeys.Cursor, ProviderKeys.Antigravity, ProviderKeys.OpenCode,
            }
            .Select(ProviderVisuals.For)
            .ToArray();

        Assert.All(visuals, visual => Assert.False(string.IsNullOrWhiteSpace(visual.IconPath)));
        Assert.Equal(visuals.Length, visuals.Select(visual => visual.IconPath).Distinct().Count());
        Assert.Equal(visuals.Length, visuals.Select(visual => visual.Accent).Distinct().Count());
    }
}
