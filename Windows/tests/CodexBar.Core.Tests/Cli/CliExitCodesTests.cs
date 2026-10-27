using CodexBar.Core.Cli;
using CodexBar.Core.Providers;
using Xunit;

namespace CodexBar.Core.Tests.Cli;

public sealed class CliExitCodesTests
{
    [Fact]
    public void All_success_is_zero() =>
        Assert.Equal(CliExitCode.Success, CliExitCodes.FromResults([Result(ProviderKeys.Codex, success: true)]));

    [Fact]
    public void Mixed_results_are_a_partial_failure()
    {
        Assert.Equal(
            CliExitCode.PartialFailure,
            CliExitCodes.FromResults(
            [
                Result(ProviderKeys.Codex, success: true),
                Result(ProviderKeys.Claude, success: false),
            ]));
    }

    [Fact]
    public void Everything_failing_is_a_total_failure()
    {
        Assert.Equal(
            CliExitCode.TotalFailure,
            CliExitCodes.FromResults([Result(ProviderKeys.Claude, success: false)]));
        Assert.Equal(CliExitCode.TotalFailure, CliExitCodes.FromResults([]));
    }

    [Fact]
    public void Require_all_promotes_a_partial_failure()
    {
        Assert.Equal(
            CliExitCode.TotalFailure,
            CliExitCodes.FromResults(
                [
                    Result(ProviderKeys.Codex, success: true),
                    Result(ProviderKeys.Claude, success: false),
                ],
                requireAll: true));
    }

    private static CliProviderResult Result(ProviderKey key, bool success) =>
        new(
            key,
            key.Value,
            "test",
            success ? new Usage.UsageSnapshot(key, DateTimeOffset.UnixEpoch) : null,
            success ? null : ProviderError.NotConfigured("missing"),
            TimeSpan.FromMilliseconds(1));
}
