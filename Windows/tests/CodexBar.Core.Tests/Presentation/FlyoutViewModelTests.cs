using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Presentation;

public sealed class FlyoutViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_provider_opens_when_nothing_is_highlighted()
    {
        var viewModel = new FlyoutViewModel(Build());

        Assert.Equal("codex", viewModel.SelectedKey);
        Assert.Equal("Codex", viewModel.Selected!.Name);
        Assert.Equal(3, viewModel.ProviderCount);
    }

    [Fact]
    public void A_highlighted_provider_opens_first_so_a_toast_click_lands_on_it()
    {
        var viewModel = new FlyoutViewModel(Build(highlightedKey: "claude"));

        Assert.Equal("claude", viewModel.SelectedKey);
        Assert.True(viewModel.Providers[1].IsSelected);
    }

    [Fact]
    public void Selecting_a_tab_moves_the_detail_and_marks_exactly_one_pill()
    {
        var viewModel = new FlyoutViewModel(Build());

        Assert.True(viewModel.Select("antigravity"));

        Assert.Equal("Antigravity", viewModel.Selected!.Name);
        Assert.Single(viewModel.Providers, static provider => provider.IsSelected);
        Assert.True(viewModel.Providers[2].IsSelected);
    }

    [Fact]
    public void Selecting_an_unknown_key_changes_nothing()
    {
        var viewModel = new FlyoutViewModel(Build());

        Assert.False(viewModel.Select("gemini"));
        Assert.False(viewModel.Select(null));
        Assert.Equal("codex", viewModel.SelectedKey);
    }

    [Fact]
    public void A_refresh_keeps_the_open_tab()
    {
        var viewModel = new FlyoutViewModel(Build());
        Assert.True(viewModel.Select("claude"));

        viewModel.Apply(Build(claudeUsage: 88));
        Assert.Equal("claude", viewModel.SelectedKey);
        Assert.Equal("88% used · 12% left", viewModel.Selected!.Windows[0].UsageText);
    }

    [Fact]
    public void A_refresh_after_the_open_provider_was_disabled_falls_back()
    {
        var viewModel = new FlyoutViewModel(Build());
        Assert.True(viewModel.Select("claude"));

        viewModel.Apply(FlyoutBuilder.Build(
            [new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now))],
            Now,
            isRefreshing: false));

        Assert.Equal("codex", viewModel.SelectedKey);
        Assert.NotNull(viewModel.Selected);
    }

    [Fact]
    public void A_new_highlight_from_a_toast_wins_over_the_kept_tab()
    {
        var viewModel = new FlyoutViewModel(Build());
        Assert.True(viewModel.Select("codex"));

        viewModel.Apply(Build(highlightedKey: "antigravity"));

        Assert.Equal("antigravity", viewModel.SelectedKey);
    }

    [Fact]
    public void No_providers_means_no_selection()
    {
        var viewModel = new FlyoutViewModel(FlyoutBuilder.Build([], Now, isRefreshing: false));

        Assert.False(viewModel.HasProviders);
        Assert.Null(viewModel.Selected);
        Assert.Equal(0, viewModel.ProviderCount);
        Assert.False(viewModel.SelectAdjacent(1));
    }

    [Fact]
    public void Arrow_keys_wrap_around_the_tabs()
    {
        var viewModel = new FlyoutViewModel(Build());

        Assert.True(viewModel.SelectAdjacent(-1));
        Assert.Equal("antigravity", viewModel.SelectedKey);

        Assert.True(viewModel.SelectAdjacent(1));
        Assert.Equal("codex", viewModel.SelectedKey);

        Assert.True(viewModel.SelectAdjacent(1));
        Assert.Equal("claude", viewModel.SelectedKey);
    }

    private static FlyoutModel Build(double usage = 42, double claudeUsage = 0, string? highlightedKey = null) =>
        FlyoutBuilder.Build(
            [
                new ProviderState(ProviderKeys.Codex, "Codex", TestSnapshots.Codex(Now, session: usage)),
                new ProviderState(ProviderKeys.Claude, "Claude", TestSnapshots.Claude(Now, session: claudeUsage)),
                new ProviderState(ProviderKeys.Antigravity, "Antigravity", TestSnapshots.Codex(Now)),
            ],
            Now,
            isRefreshing: false,
            highlightedKey);
}
