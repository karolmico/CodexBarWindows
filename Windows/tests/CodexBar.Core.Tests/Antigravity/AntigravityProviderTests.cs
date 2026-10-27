using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Antigravity;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Antigravity;

public sealed class AntigravityProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private const string AppCommand =
        "C:\\Users\\Test\\AppData\\Local\\Programs\\Antigravity\\resources\\app\\language_server.exe "
        + "--app_data_dir antigravity --csrf_token token-123 --extension_server_port 51234";

    private const string AppCommandWithoutPort =
        "C:\\Users\\Test\\AppData\\Local\\Programs\\Antigravity\\resources\\app\\language_server.exe "
        + "--app_data_dir antigravity --csrf_token token-123";

    private static readonly string CliReport = Fixture.Read(Path.Combine("Antigravity", "cli-usage.json"));

    private static AntigravityUsageClient HealthyClient()
    {
        var handler = new RoutingHandler([])
            .Add(
                AntigravityUsageClient.QuotaSummaryPath,
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("Antigravity", "quota-summary.json")))
            .Add(
                AntigravityUsageClient.UserStatusPath,
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("Antigravity", "user-status.json")));
        return new AntigravityUsageClient(new HttpClient(handler, disposeHandler: false), () => Now);
    }

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = new AntigravityProvider(new FakeProcessSource([]), HealthyClient());

        Assert.Equal("antigravity", provider.Descriptor.Key.Value);
        Assert.Equal("Antigravity", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task Closed_client_without_the_cli_reports_not_running()
    {
        var provider = new AntigravityProvider(new FakeProcessSource([]), HealthyClient());

        var result = await provider.FetchAsync(CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Contains("isn't running", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closed_client_falls_back_to_the_cli()
    {
        var cli = new FakeCliSource(new UsageFetchResult.Success(CliSnapshot()));
        var provider = new AntigravityProvider(new FakeProcessSource([]), HealthyClient(), cli);

        var result = await provider.FetchAsync(CancellationToken.None);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(result).Snapshot;
        Assert.Equal("antigravity", snapshot.Provider.Value);
        Assert.Equal(1, cli.Calls);
    }

    [Fact]
    public async Task Running_client_prefers_the_local_language_server()
    {
        var source = new FakeProcessSource(
            [new AntigravityProcessInfo(4242, AppCommand)],
            ports: new Dictionary<int, int[]> { [4242] = [51234] });
        var cli = new FakeCliSource(new UsageFetchResult.Success(CliSnapshot()));
        var provider = new AntigravityProvider(source, HealthyClient(), cli);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(58d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal("user@example.com", snapshot.Identity!.AccountLabel);
        Assert.Equal(0, cli.Calls);
    }

    [Fact]
    public async Task Failing_language_server_still_falls_back_to_the_cli()
    {
        var source = new FakeProcessSource(
            [new AntigravityProcessInfo(4242, AppCommand)],
            ports: new Dictionary<int, int[]> { [4242] = [51234] });
        var brokenClient = new AntigravityUsageClient(
            new HttpClient(new RoutingHandler([]), disposeHandler: false),
            () => Now);
        var cli = new FakeCliSource(new UsageFetchResult.Success(CliSnapshot()));
        var provider = new AntigravityProvider(source, brokenClient, cli);

        Assert.IsType<UsageFetchResult.Success>(await provider.FetchAsync(CancellationToken.None));
        Assert.Equal(1, cli.Calls);
    }

    [Fact]
    public async Task Untokened_app_uses_the_cli_instead_of_a_doomed_probe()
    {
        var source = new FakeProcessSource(
        [
            new AntigravityProcessInfo(
                12,
                "C:\\Antigravity\\extensions\\antigravity\\bin\\language_server.exe --app_data_dir antigravity-ide"),
        ]);
        var cli = new FakeCliSource(new UsageFetchResult.Success(CliSnapshot()));
        var provider = new AntigravityProvider(source, HealthyClient(), cli);

        Assert.IsType<UsageFetchResult.Success>(await provider.FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Untokened_app_without_the_cli_asks_for_a_restart()
    {
        var source = new FakeProcessSource(
        [
            new AntigravityProcessInfo(
                12,
                "C:\\Antigravity\\extensions\\antigravity\\bin\\language_server.exe --app_data_dir antigravity-ide"),
        ]);
        var provider = new AntigravityProvider(source, HealthyClient());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Contains("token was not found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Running_client_without_ports_is_a_network_error()
    {
        var source = new FakeProcessSource(
            [new AntigravityProcessInfo(4242, AppCommandWithoutPort)],
            ports: new Dictionary<int, int[]> { [4242] = [] });
        var provider = new AntigravityProvider(source, HealthyClient());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
        Assert.Contains("usage port", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hand_started_cli_session_is_not_probed()
    {
        var source = new FakeProcessSource(
            [new AntigravityProcessInfo(99, "C:\\Users\\Test\\AppData\\Local\\agy\\bin\\agy.exe --session local")]);
        var provider = new AntigravityProvider(source, HealthyClient());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
    }

    [Fact]
    public async Task Neither_source_available_names_the_cli_in_the_copy()
    {
        var provider = new AntigravityProvider(
            new FakeProcessSource([]),
            HealthyClient(),
            new FakeCliSource(null));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Contains("`agy`", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_failure_is_reported_when_no_app_is_running()
    {
        var failure = new UsageFetchResult.Failure(ProviderError.AuthenticationRequired(
            "The Antigravity CLI isn't signed in."));
        var provider = new AntigravityProvider(
            new FakeProcessSource([]),
            HealthyClient(),
            new FakeCliSource(failure));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
    }

    /// <summary>A minimal one-lane snapshot for sources that only need to prove they answered.</summary>
    private static CodexBar.Core.Usage.UsageSnapshot CliSnapshot() =>
        new(ProviderKeys.Antigravity, Now, new CodexBar.Core.Usage.RateWindow(10d, TimeSpan.FromMinutes(300)));
    private sealed class FakeCliSource(UsageFetchResult? result) : IAntigravityCliSource
    {
        public int Calls { get; private set; }

        public Task<UsageFetchResult?> TryFetchAsync(CancellationToken cancellationToken)
        {
            this.Calls++;
            return Task.FromResult(result);
        }
    }


    private sealed class FakeProcessSource(
        IReadOnlyList<AntigravityProcessInfo> processes,
        IReadOnlyDictionary<int, int[]>? ports = null) : IAntigravityProcessSource
    {
        public Task<IReadOnlyList<AntigravityProcessInfo>> ListProcessesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(processes);

        public Task<IReadOnlyList<int>> ListeningPortsAsync(int pid, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>>(ports is not null && ports.TryGetValue(pid, out var found) ? found : []);
    }
}
