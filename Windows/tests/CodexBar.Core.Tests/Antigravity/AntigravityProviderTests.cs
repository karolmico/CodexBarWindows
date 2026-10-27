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
    public async Task Closed_client_reports_not_running()
    {
        var provider = new AntigravityProvider(new FakeProcessSource([]), HealthyClient());

        var result = await provider.FetchAsync(CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Contains("isn't running", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Running_client_without_a_token_asks_for_a_restart()
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
        [
            new AntigravityProcessInfo(
                99,
                "C:\\Users\\Test\\AppData\\Local\\Programs\\agy\\agy.exe --session local"),
        ]);
        var provider = new AntigravityProvider(source, HealthyClient());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
        Assert.Contains("usage port", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Healthy_probe_returns_the_mapped_snapshot()
    {
        var source = new FakeProcessSource(
            [new AntigravityProcessInfo(4242, AppCommand)],
            ports: new Dictionary<int, int[]> { [4242] = [51234] });
        var provider = new AntigravityProvider(source, HealthyClient());

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(58d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal("user@example.com", snapshot.Identity!.AccountLabel);
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
