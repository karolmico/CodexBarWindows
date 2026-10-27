using CodexBar.Core.Notifications;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Settings;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests.Notifications;

public sealed class NotificationDeciderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly AppSettings Defaults = new();

    [Fact]
    public void Each_threshold_fires_once_until_usage_drops_below_it()
    {
        var decider = new NotificationDecider();

        Assert.Empty(Evaluate(decider, 70));
        var first = Assert.Single(Evaluate(decider, 82));
        Assert.Equal(80, first.Threshold);
        Assert.Equal(UsageNotificationKind.Threshold, first.Kind);
        Assert.Empty(Evaluate(decider, 85));
        Assert.Empty(Evaluate(decider, 89));
        var second = Assert.Single(Evaluate(decider, 96));
        Assert.Equal(95, second.Threshold);
        Assert.Empty(Evaluate(decider, 97));
        Assert.Empty(Evaluate(decider, 50));
        Assert.Equal(80, Assert.Single(Evaluate(decider, 82)).Threshold);
    }

    [Fact]
    public void A_jump_across_both_thresholds_emits_only_the_highest()
    {
        var decider = new NotificationDecider();

        var notification = Assert.Single(Evaluate(decider, 96));

        Assert.Equal(95, notification.Threshold);
    }

    [Fact]
    public void Notification_copy_names_the_provider_and_window()
    {
        var decider = new NotificationDecider();

        var notification = Assert.Single(Evaluate(decider, 82));

        Assert.Equal("Codex at 80%", notification.Title);
        Assert.Equal("Session is 82% used.", notification.Message);
        Assert.True(notification.IsWarning);
    }

    [Fact]
    public void A_reset_advancing_emits_once_and_rearms_the_thresholds()
    {
        var decider = new NotificationDecider();
        var firstReset = Now.AddHours(3);
        var secondReset = Now.AddHours(8);

        Assert.Empty(Evaluate(decider, 70, firstReset));
        var reset = Assert.Single(Evaluate(decider, 0, secondReset));
        Assert.Equal(UsageNotificationKind.Reset, reset.Kind);
        Assert.Equal("Codex reset", reset.Title);
        Assert.Equal("Session reset: 0% used.", reset.Message);
        Assert.Empty(Evaluate(decider, 0, secondReset));
        Assert.Equal(80, Assert.Single(Evaluate(decider, 82, secondReset)).Threshold);
    }

    [Fact]
    public void A_reset_without_usage_drop_still_rearms_and_does_not_stack_a_threshold()
    {
        var decider = new NotificationDecider();

        Assert.Empty(Evaluate(decider, 70, Now.AddHours(3)));
        var reset = Assert.Single(Evaluate(decider, 96, Now.AddHours(8)));

        Assert.Equal(UsageNotificationKind.Reset, reset.Kind);
        Assert.Empty(Evaluate(decider, 96, Now.AddHours(8)));
    }

    [Fact]
    public void Disabled_reset_category_keeps_threshold_alerts_armed()
    {
        var decider = new NotificationDecider();
        var settings = new AppSettings(notifyResets: false);

        Assert.Empty(decider.Evaluate([State(70, Now.AddHours(3))], settings, Now));
        Assert.Empty(decider.Evaluate([State(0, Now.AddHours(8))], settings, Now));
        Assert.Equal(80, Assert.Single(decider.Evaluate([State(82, Now.AddHours(8))], settings, Now)).Threshold);
    }

    [Fact]
    public void Disabled_thresholds_produce_no_threshold_toasts()
    {
        var decider = new NotificationDecider();
        var settings = new AppSettings(notificationThresholds: []);

        Assert.Empty(decider.Evaluate([State(99)], settings, Now));
        Assert.Empty(decider.Evaluate([State(99)], settings, Now));
    }

    [Fact]
    public void Quiet_hours_suppress_delivery_without_replaying_later()
    {
        var decider = new NotificationDecider();
        var settings = new AppSettings(quietHoursStart: new TimeOnly(22, 0), quietHoursEnd: new TimeOnly(7, 0));
        var night = new DateTimeOffset(2026, 10, 27, 23, 30, 0, TimeSpan.Zero);
        var morning = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

        Assert.Empty(decider.Evaluate([State(96)], settings, night));
        Assert.Empty(decider.Evaluate([State(96)], settings, morning));
        Assert.Empty(decider.Evaluate([State(10)], settings, morning));
        Assert.Equal(80, Assert.Single(decider.Evaluate([State(82)], settings, morning)).Threshold);
    }

    [Fact]
    public void A_provider_error_never_notifies()
    {
        var decider = new NotificationDecider();
        var failing = new ProviderState(
            ProviderKeys.Codex,
            "Codex",
            State(99).Snapshot,
            ProviderError.AuthenticationRequired("expired"));

        Assert.Empty(decider.Evaluate([failing], Defaults, Now));
    }

    [Fact]
    public void Extra_windows_are_tracked_separately_from_the_session()
    {
        var decider = new NotificationDecider();
        var snapshot = new UsageSnapshot(
            ProviderKeys.Gemini,
            Now,
            primary: new RateWindow(10, TimeSpan.FromHours(5), Now.AddHours(2)),
            extraWindows:
            [
                new NamedRateWindow("flash-lite", "Flash Lite", new RateWindow(97, TimeSpan.FromDays(1), Now.AddHours(4))),
            ]);

        var state = new ProviderState(ProviderKeys.Gemini, "Gemini", snapshot);
        var notification = Assert.Single(decider.Evaluate([state], Defaults, Now));

        Assert.Equal("Flash Lite", notification.WindowTitle);
        Assert.Equal(95, notification.Threshold);
        Assert.Empty(decider.Evaluate([state], Defaults, Now));
    }

    [Fact]
    public void Synthetic_placeholders_are_ignored()
    {
        var decider = new NotificationDecider();
        var snapshot = new UsageSnapshot(
            ProviderKeys.Cursor,
            Now,
            primary: new RateWindow(99, isSyntheticPlaceholder: true));

        var state = new ProviderState(ProviderKeys.Cursor, "Cursor", snapshot);

        Assert.Empty(decider.Evaluate([state], Defaults, Now));
    }

    [Theory]
    [InlineData("23:30", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("21:59", false)]
    public void Quiet_hours_wrap_past_midnight(string time, bool expected)
    {
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(7, 0);
        var at = DateTimeOffset.Parse($"2026-10-27T{time}:00Z");

        Assert.Equal(expected, NotificationDecider.IsQuietHours(at, start, end));
    }

    [Fact]
    public void Quiet_hours_normally_compare_within_the_same_day()
    {
        var start = new TimeOnly(9, 0);
        var end = new TimeOnly(17, 0);

        Assert.True(NotificationDecider.IsQuietHours(DateTimeOffset.Parse("2026-10-27T12:00:00Z"), start, end));
        Assert.False(NotificationDecider.IsQuietHours(DateTimeOffset.Parse("2026-10-27T18:00:00Z"), start, end));
        Assert.False(NotificationDecider.IsQuietHours(DateTimeOffset.Parse("2026-10-27T12:00:00Z"), null, null));
    }

    private static IReadOnlyList<UsageNotification> Evaluate(
        NotificationDecider decider,
        double session,
        DateTimeOffset? resetsAt = null) =>
        decider.Evaluate([State(session, resetsAt)], Defaults, Now);

    private static ProviderState State(double session, DateTimeOffset? resetsAt = null) =>
        new(
            ProviderKeys.Codex,
            "Codex",
            new UsageSnapshot(
                ProviderKeys.Codex,
                Now,
                primary: new RateWindow(session, TimeSpan.FromHours(5), resetsAt ?? Now.AddHours(3))));
}
