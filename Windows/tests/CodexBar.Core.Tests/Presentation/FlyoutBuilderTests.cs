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
        Assert.Equal("42% used · 58% left", card.Windows[0].UsageText);
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
    public void Every_lane_states_used_and_left_so_the_native_remaining_view_matches()
    {
        // Antigravity's own window shows "0% remaining"; the flyout must read the same way.
        var snapshot = new UsageSnapshot(
            ProviderKeys.Antigravity,
            Now,
            primary: new RateWindow(100, TimeSpan.FromHours(5), Now.AddHours(1)));

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", snapshot)],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal("100% used · 0% left", card.Windows[0].UsageText);
        Assert.True(card.Windows[0].IsExhausted);
    }

    [Fact]
    public void A_disabled_lane_stays_visible_with_the_providers_reason()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Antigravity,
            Now,
            primary: new RateWindow(100, TimeSpan.FromHours(5), Now.AddHours(1)),
            extraWindows:
            [
                new NamedRateWindow(
                    "antigravity-claude-gpt-5h",
                    "Claude/GPT 5-hour",
                    new RateWindow(
                        0,
                        TimeSpan.FromHours(5),
                        isDisabled: true,
                        disabledReason: "You have hit your weekly limit, the 5-hour limit does not currently apply."),
                    usageKnown: false),
            ]);

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", snapshot)],
            Now,
            isRefreshing: false).Providers);

        var lane = Assert.Single(card.Windows, window => window.IsDisabled);
        Assert.Equal("Claude/GPT 5-hour", lane.Title);
        Assert.Equal("Disabled", lane.UsageText);
        Assert.StartsWith("You have hit your weekly limit", lane.NoteText, StringComparison.Ordinal);
        Assert.False(lane.ShowBar);
        Assert.False(lane.IsExhausted);
        Assert.Null(lane.PaceText);
        Assert.Equal(string.Empty, lane.ResetText);
    }

    [Fact]
    public void A_disabled_lane_without_a_reason_still_says_it_is_disabled()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Antigravity,
            Now,
            primary: new RateWindow(10, TimeSpan.FromHours(5), Now.AddHours(1)),
            extraWindows:
            [
                new NamedRateWindow(
                    "antigravity-claude-gpt-5h",
                    "Claude/GPT 5-hour",
                    new RateWindow(0, isDisabled: true),
                    usageKnown: false),
            ]);

        var lane = Assert.Single(
            Assert.Single(FlyoutBuilder.Build(
                [new ProviderState(ProviderKeys.Antigravity, "Antigravity", snapshot)],
                Now,
                isRefreshing: false).Providers).Windows,
            window => window.IsDisabled);

        Assert.Equal("Disabled", lane.UsageText);
        Assert.Null(lane.NoteText);
    }

    [Fact]
    public void Antigravity_lanes_group_under_their_family_headers()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Antigravity,
            Now,
            primary: new RateWindow(12, TimeSpan.FromDays(7), Now.AddDays(1), title: "weekly", group: "Gemini Models"),
            secondary: new RateWindow(100, TimeSpan.FromDays(7), Now.AddDays(2), title: "weekly", group: "Claude and GPT models"),
            extraWindows:
            [
                new NamedRateWindow(
                    "a",
                    "Gemini 5-hour",
                    new RateWindow(11, TimeSpan.FromHours(5), Now.AddHours(2), title: "5-hour", group: "Gemini Models")),
                new NamedRateWindow(
                    "b",
                    "Claude/GPT 5-hour",
                    new RateWindow(0, TimeSpan.FromHours(5), isDisabled: true, title: "5-hour", group: "Claude and GPT models"),
                    usageKnown: false),
                new NamedRateWindow(
                    "c",
                    "Gemini 4 Argon weekly",
                    new RateWindow(0, TimeSpan.FromDays(7), Now.AddDays(7), title: "weekly", group: "Gemini 4 Argon")),
            ]);

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", snapshot)],
            Now,
            isRefreshing: false).Providers);

        var windows = card.Windows;
        Assert.Equal(5, windows.Count);

        Assert.Equal("Gemini Models", windows[0].GroupHeader);
        Assert.Equal("weekly", windows[0].Title);
        Assert.Null(windows[1].GroupHeader);
        Assert.Equal("5-hour", windows[1].Title);

        Assert.Equal("Claude and GPT models", windows[2].GroupHeader);
        Assert.Equal("weekly", windows[2].Title);
        Assert.Null(windows[3].GroupHeader);
        Assert.Equal("5-hour", windows[3].Title);
        Assert.True(windows[3].IsDisabled);

        Assert.Equal("Gemini 4 Argon", windows[4].GroupHeader);
        Assert.Equal("weekly", windows[4].Title);
    }

    [Fact]
    public void Providers_without_groups_do_not_gain_headers()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, session: 42, weekly: 61))],
            Now,
            isRefreshing: false).Providers);

        Assert.All(card.Windows, static window => Assert.Null(window.GroupHeader));
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
