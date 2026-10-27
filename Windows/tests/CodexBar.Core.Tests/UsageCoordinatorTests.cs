using CodexBar.Core.Providers;
using CodexBar.Core.Refresh;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class UsageCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_with_one_state_per_provider_in_registration_order()
    {
        var coordinator = Coordinator(
            new ScriptedProvider(ProviderKeys.Codex, "Codex"),
            new ScriptedProvider(ProviderKeys.Claude, "Claude"));

        Assert.Equal(["Codex", "Claude"], coordinator.States.Select(state => state.DisplayName));
        Assert.All(coordinator.States, state => Assert.Null(state.Snapshot));
        Assert.All(coordinator.States, state => Assert.Null(state.Error));
    }

    [Fact]
    public async Task Successful_refresh_publishes_the_snapshot_and_clears_the_error()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Error(ProviderErrorKind.Network, "offline"));
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now, session: 42d));
        var coordinator = Coordinator(provider);

        await coordinator.RefreshAsync(CancellationToken.None);
        Assert.NotNull(coordinator.States[0].Error);

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Null(coordinator.States[0].Error);
        Assert.Equal(42d, coordinator.States[0].Snapshot!.Primary!.UsedPercent);
    }

    [Fact]
    public async Task Failure_keeps_the_last_good_snapshot_and_attaches_the_error()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now, session: 42d));
        provider.WillReturn(ScriptedProvider.Error(ProviderErrorKind.Network, "offline"));
        var coordinator = Coordinator(provider);

        await coordinator.RefreshAsync(CancellationToken.None);
        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(42d, coordinator.States[0].Snapshot!.Primary!.UsedPercent);
        Assert.Equal(ProviderErrorKind.Network, coordinator.States[0].Error!.Kind);
    }

    [Fact]
    public async Task Consecutive_failures_increase_the_backoff()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Error(ProviderErrorKind.Network, "offline"));
        provider.WillReturn(ScriptedProvider.Error(ProviderErrorKind.Network, "offline"));
        var coordinator = Coordinator(provider);

        await coordinator.RefreshAsync(CancellationToken.None);
        var firstDelay = coordinator.NextDelay();
        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(30), firstDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), coordinator.NextDelay());
    }

    [Fact]
    public async Task Next_delay_follows_the_usage_pressure_after_a_success()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now, session: 96d));
        var coordinator = Coordinator(provider);

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(1), coordinator.NextDelay());
    }

    [Fact]
    public async Task Concurrent_refresh_requests_coalesce_into_one_fetch_per_provider()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now));
        provider.BlockUntilReleased();
        var coordinator = Coordinator(provider);

        var first = coordinator.RefreshAsync(CancellationToken.None);
        var second = coordinator.RefreshAsync(CancellationToken.None);
        Assert.Same(first, second);
        Assert.True(coordinator.IsRefreshing);

        provider.Release();
        await first;

        Assert.False(coordinator.IsRefreshing);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Reports_changes_around_a_refresh()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now));
        var coordinator = Coordinator(provider);
        var notifications = 0;
        coordinator.Changed += (_, _) => notifications++;

        await coordinator.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Cached_snapshots_are_shown_immediately_and_schedule_a_refresh()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now, session: 42d));
        var coordinator = Coordinator(provider);
        var cached = TestSnapshots.Codex(Now.AddHours(-2), session: 35d);

        coordinator.ApplyCached([cached]);

        Assert.Equal(35d, coordinator.States[0].Snapshot!.Primary!.UsedPercent);
        Assert.Null(coordinator.States[0].Error);
        Assert.Equal(TimeSpan.Zero, coordinator.NextDelay());
        Assert.Single(coordinator.Snapshots);
    }

    [Fact]
    public void Cached_snapshots_for_unregistered_providers_are_ignored()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        var coordinator = Coordinator(provider);

        coordinator.ApplyCached([TestSnapshots.Claude(Now.AddHours(-1), 10d)]);

        Assert.Null(coordinator.States[0].Snapshot);
        Assert.Empty(coordinator.Snapshots);
    }

    private static UsageCoordinator Coordinator(params IUsageProvider[] providers) =>
        new(providers, new RefreshPolicy(), () => Now);
}
