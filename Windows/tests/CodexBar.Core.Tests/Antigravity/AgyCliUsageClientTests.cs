using System.IO;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Antigravity;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Antigravity;

public sealed class AgyCliUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static string Report(string name) => Fixture.Read(Path.Combine("Antigravity", name));

    private static FakeAgyRunner Installed(string? report = null) =>
        new()
        {
            ["--version"] = AgyRunResult.Completed(0, "1.4.8\n", string.Empty),
            [ArgumentsKey(AgyCliUsageClient.UsageArguments)] = AgyRunResult.Completed(
                0,
                report ?? Report("cli-usage.json"),
                string.Empty),
        };

    private static string ArgumentsKey(IReadOnlyList<string> arguments) => string.Join(' ', arguments);

    [Fact]
    public async Task Healthy_report_maps_gemini_claude_and_extras()
    {
        var result = await new AgyCliUsageClient(Installed(), () => Now).TryFetchAsync(CancellationToken.None);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(result).Snapshot;
        Assert.Equal(22.452557d, snapshot.Primary!.UsedPercent, precision: 4);
        Assert.Equal(TimeSpan.FromMinutes(300), snapshot.Primary.Window);
        Assert.Equal(100d, snapshot.Secondary!.UsedPercent, precision: 4);
        Assert.True(snapshot.IsExhausted);

        var extras = snapshot.ExtraWindows.ToDictionary(window => window.Id, StringComparer.Ordinal);
        Assert.Equal(3, extras.Count);
        Assert.Equal(9.752905d, extras["antigravity-gemini-gemini-weekly"].Window.UsedPercent, precision: 4);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 27, 19, 56, 5, TimeSpan.Zero),
            extras["antigravity-gemini-gemini-weekly"].Window.ResetsAt);
        Assert.False(extras["antigravity-claude-gpt-3p-5h"].UsageKnown);
        Assert.Equal("Gemini 4 Argon weekly", extras["antigravity-gemini-4-argon-gemini-argon-wtu-weekly"].Title);
        Assert.Equal(0d, extras["antigravity-gemini-4-argon-gemini-argon-wtu-weekly"].Window.UsedPercent);
    }

    [Fact]
    public async Task Missing_binary_is_reported_as_not_installed()
    {
        var runner = new FakeAgyRunner { ["--version"] = AgyRunResult.NotInstalled() };

        var result = await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(runner.Invocations, arguments => arguments[0] == "-p");
    }

    [Fact]
    public async Task Old_cli_is_refused_without_running_the_report()
    {
        var runner = new FakeAgyRunner { ["--version"] = AgyRunResult.Completed(0, "1.1.10", string.Empty) };

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Unknown, error.Kind);
        Assert.Contains("too old", error.Message, StringComparison.Ordinal);
        Assert.Single(runner.Invocations);
    }

    [Fact]
    public async Task Unreadable_version_is_a_parse_error()
    {
        var runner = new FakeAgyRunner { ["--version"] = AgyRunResult.Completed(0, "agy dev build", string.Empty) };

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Signed_out_cli_asks_for_a_sign_in_and_hides_stderr()
    {
        var runner = Installed();
        runner[ArgumentsKey(AgyCliUsageClient.UsageArguments)] = AgyRunResult.Completed(
            1,
            string.Empty,
            "Opening browser to https://accounts.google.com/profile/photo?user=someone@example.com\n"
            + "You are not signed in.");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("`agy`", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Network_failure_in_stderr_is_a_network_error()
    {
        var runner = Installed();
        runner[ArgumentsKey(AgyCliUsageClient.UsageArguments)] = AgyRunResult.Completed(
            1,
            string.Empty,
            "Get \"https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist\": proxyconnect tcp: dial tcp: i/o timeout");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Timeout_is_a_network_error()
    {
        var runner = Installed();
        runner[ArgumentsKey(AgyCliUsageClient.UsageArguments)] = AgyRunResult.TimedOutAfter();

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(runner, () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
        Assert.Contains("did not answer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_report_is_a_parse_error()
    {
        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(Installed("{\"status\": \"SUCCESS\","), () => Now)
                .TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Failed_report_status_is_a_parse_error()
    {
        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(
                Installed("{\"status\":\"ERROR\",\"command\":{\"name\":\"usage\",\"data\":{}}}"),
                () => Now).TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Report_with_every_bucket_disabled_has_no_usable_quota()
    {
        var error = Assert.IsType<UsageFetchResult.Failure>(
            await new AgyCliUsageClient(Installed(Report("cli-usage-no-quota.json")), () => Now)
                .TryFetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
        Assert.Contains("no usable quota", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_probe_runs_once_per_client()
    {
        var runner = Installed();
        var client = new AgyCliUsageClient(runner, () => Now);

        await client.TryFetchAsync(CancellationToken.None);
        await client.TryFetchAsync(CancellationToken.None);

        Assert.Single(runner.Invocations, arguments => arguments.Count == 1 && arguments[0] == "--version");
        Assert.Equal(2, runner.Invocations.Count(arguments => arguments[0] == "-p"));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var runner = Installed();
        using var source = new CancellationTokenSource();
        runner.OnRun = _ => source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AgyCliUsageClient(runner, () => Now).TryFetchAsync(source.Token));
    }

    private sealed class FakeAgyRunner : IAgyProcessRunner
    {
        private readonly Dictionary<string, AgyRunResult> results = new(StringComparer.Ordinal);

        public AgyRunResult this[string arguments]
        {
            set => this.results[arguments] = value;
        }

        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public Action<IReadOnlyList<string>>? OnRun { get; set; }

        public Task<AgyRunResult> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Invocations.Add(arguments);
            this.OnRun?.Invoke(arguments);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                this.results.TryGetValue(string.Join(' ', arguments), out var result)
                    ? result
                    : AgyRunResult.NotInstalled());
        }
    }
}
