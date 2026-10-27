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
        Assert.Equal("Plus", card.Plan);
        Assert.Null(card.Status);
        Assert.Equal(["Session", "Weekly", "Flash Lite"], card.Windows.Select(window => window.Title));
        Assert.Equal(42d, card.Windows[0].UsedPercent);
        Assert.Equal("42% used", card.Windows[0].UsageText);
        Assert.StartsWith("resets in", card.Windows[0].ResetText, StringComparison.Ordinal);
        Assert.False(card.IsError);
        Assert.StartsWith("Updated ", model.FooterText, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_with_a_cached_snapshot_keeps_the_windows_and_shows_the_status()
    {
        var snapshot = TestSnapshots.Claude(Now.AddMinutes(-10), session: 90, weekly: 30);

        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Claude, "Claude", snapshot, ProviderError.AuthenticationRequired("expired"))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal(2, card.Windows.Count);
        Assert.True(card.IsError);
        Assert.Equal("sign-in required", card.Status);
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
        Assert.False(card.IsError);
    }

    [Fact]
    public void Provider_without_a_snapshot_reports_the_error_label()
    {
        var card = Assert.Single(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Antigravity, "Antigravity", error: ProviderError.NotConfigured("closed"))],
            Now,
            isRefreshing: false).Providers);

        Assert.Equal("not configured", card.Status);
        Assert.True(card.IsError);
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
}
