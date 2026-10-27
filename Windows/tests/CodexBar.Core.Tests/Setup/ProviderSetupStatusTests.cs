using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Setup;
using CodexBar.Core.Tests.Support;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests.Setup;

public sealed class ProviderSetupStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_provider_without_a_result_is_reported_as_not_checked()
    {
        Assert.Equal(ProviderSetupStatus.Unknown, ProviderSetupStatusText.Describe(null, Now));
        Assert.Equal(
            ProviderSetupStatus.Unknown,
            ProviderSetupStatusText.Describe(new ProviderState(ProviderKeys.Codex, "Codex"), Now));
    }

    [Fact]
    public void An_error_becomes_a_problem_line_with_the_existing_status_label()
    {
        var state = new ProviderState(
            ProviderKeys.Claude,
            "Claude",
            error: ProviderError.NotConfigured("Claude credentials not found. Run `claude` to sign in."));

        var status = ProviderSetupStatusText.Describe(state, Now);

        Assert.True(status.IsProblem);
        Assert.False(status.IsConnected);
        Assert.StartsWith("not configured:", status.Text, StringComparison.Ordinal);
        Assert.Contains("Run `claude`", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_snapshot_reports_success_and_how_old_it_is()
    {
        var snapshot = TestSnapshots.Codex(Now.AddMinutes(-3), session: 42d);
        var state = new ProviderState(ProviderKeys.Codex, "Codex", snapshot);

        var status = ProviderSetupStatusText.Describe(state, Now);

        Assert.True(status.IsConnected);
        Assert.False(status.IsProblem);
        Assert.StartsWith("Connected.", status.Text, StringComparison.Ordinal);
        Assert.Contains("3m ago", status.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_without_quota_windows_still_counts_as_connected()
    {
        var snapshot = new UsageSnapshot(ProviderKeys.Copilot, Now);
        var state = new ProviderState(ProviderKeys.Copilot, "Copilot", snapshot);

        var status = ProviderSetupStatusText.Describe(state, Now);

        Assert.True(status.IsConnected);
        Assert.Contains("reports no quota windows", status.Text, StringComparison.Ordinal);
    }
}
