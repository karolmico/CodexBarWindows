using CodexBar.Core.Cli;
using CodexBar.Core.Providers;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Cli;

public sealed class CliUsageRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unexpected_provider_exceptions_become_unknown_errors_without_failing_the_run()
    {
        var throwing = new ThrowingProvider();
        var healthy = new ScriptedProvider(ProviderKeys.Claude, "Claude");
        healthy.WillReturn(ScriptedProvider.Ok(ProviderKeys.Claude, Now, session: 42d));

        var results = await new CliUsageRunner().RunAsync([throwing, healthy], CancellationToken.None);

        Assert.Equal(2, results.Count);
        var failure = results[0];
        Assert.False(failure.IsSuccess);
        Assert.Equal(ProviderErrorKind.Unknown, failure.Error!.Kind);
        Assert.Contains("Boom", failure.Error.Cause!.Message, StringComparison.Ordinal);
        Assert.NotNull(failure.Duration);
        Assert.True(results[1].IsSuccess);
    }

    [Fact]
    public async Task Success_uses_the_stable_source_label_and_keeps_the_raw_snapshot()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.WillReturn(ScriptedProvider.Ok(ProviderKeys.Codex, Now, session: 42d, weekly: 61d));

        var result = Assert.Single(
            await new CliUsageRunner().RunAsync([provider], CancellationToken.None));

        Assert.True(result.IsSuccess);
        Assert.Equal("codex-cli", result.Source);
        Assert.Equal(42d, result.Snapshot!.Primary!.UsedPercent);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_provider_error()
    {
        var provider = new ScriptedProvider(ProviderKeys.Codex, "Codex");
        provider.BlockUntilReleased();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CliUsageRunner().RunAsync([provider], cancellation.Token));
    }

    private sealed class ThrowingProvider : IUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Copilot, "Copilot");

        public Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Boom from a provider.");
    }
}
