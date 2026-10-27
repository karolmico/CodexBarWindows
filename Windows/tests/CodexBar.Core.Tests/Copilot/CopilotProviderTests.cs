using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Copilot;
using CodexBar.Core.Tests.Support;
using System.Net;
using Xunit;

namespace CodexBar.Core.Tests.Copilot;

public sealed class CopilotTokenSourceTests
{
    private static CopilotTokenSource Source(
        Dictionary<string, string> environment,
        Func<CancellationToken, Task<string?>>? gitHubCli = null,
        IProviderTokenStore? tokenStore = null) =>
        new(() => environment, gitHubCli ?? (_ => Task.FromResult<string?>(null)), tokenStore);

    [Fact]
    public async Task Prefers_an_explicit_environment_token()
    {
        var source = Source(
            new Dictionary<string, string> { ["GITHUB_TOKEN"] = " ghp_env ", ["GH_TOKEN"] = "ghp_other" },
            _ => Task.FromResult<string?>("ghp_cli"));

        Assert.Equal("ghp_env", await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_token_saved_in_codexbar_wins_over_environment_and_cli()
    {
        var store = new FakeTokenStore();
        store.SaveToken(ProviderKeys.Copilot, "ghp_saved");
        var source = Source(
            new Dictionary<string, string> { ["GITHUB_TOKEN"] = "ghp_env" },
            _ => Task.FromResult<string?>("ghp_cli"),
            store);

        Assert.Equal("ghp_saved", await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_saved_token_falls_back_to_the_environment()
    {
        var store = new FakeTokenStore();
        store.SaveToken(ProviderKeys.Copilot, "   ");
        var source = Source(new Dictionary<string, string> { ["GITHUB_TOKEN"] = "ghp_env" }, tokenStore: store);

        Assert.Equal("ghp_env", await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_saved_token_for_another_provider_is_ignored()
    {
        var store = new FakeTokenStore();
        store.SaveToken(ProviderKeys.Claude, "not-a-github-token");
        var source = Source(new Dictionary<string, string>(), tokenStore: store);

        Assert.Null(await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Falls_back_to_the_github_cli()
    {
        var source = Source(new Dictionary<string, string>(), _ => Task.FromResult<string?>(" ghp_cli \n"));

        Assert.Equal("ghp_cli", await source.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_when_neither_source_has_a_token()
    {
        var source = Source(new Dictionary<string, string>());

        Assert.Null(await source.ResolveAsync(CancellationToken.None));
    }

    private sealed class FakeTokenStore : IProviderTokenStore
    {
        private readonly Dictionary<ProviderKey, string> tokens = [];

        public string? GetToken(ProviderKey provider) =>
            this.tokens.TryGetValue(provider, out var token) ? token : null;

        public void SaveToken(ProviderKey provider, string token) => this.tokens[provider] = token;

        public void ClearToken(ProviderKey provider) => this.tokens.Remove(provider);
    }
}

public sealed class CopilotUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static (CopilotUsageClient Client, StubHttpHandler Handler) ClientWith(
        HttpStatusCode status,
        string body)
    {
        var handler = new StubHttpHandler(status, body);
        return (new CopilotUsageClient(StubHttpHandler.Client(handler), () => Now), handler);
    }

    [Fact]
    public async Task Calls_the_copilot_endpoint_with_a_token_authorization()
    {
        var (client, handler) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Copilot", "copilot-metered.json")));

        await client.FetchAsync("ghp_test", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("api.github.com", request.RequestUri!.Host);
        Assert.Equal("/copilot_internal/user", request.RequestUri.AbsolutePath);
        Assert.Equal("token ghp_test", request.Headers.GetValues("Authorization").Single());
        Assert.StartsWith("CodexBar", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_premium_request_usage_and_plan()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Copilot", "copilot-metered.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync("ghp_test", CancellationToken.None)).Snapshot;

        Assert.Equal("copilot", snapshot.Provider.Value);
        Assert.Equal(58d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:00:00Z"), snapshot.Primary.ResetsAt);
        Assert.Null(snapshot.Secondary);
        Assert.Equal("Individual", snapshot.Identity!.PlanName);
    }

    [Fact]
    public async Task Unlimited_or_token_based_seat_reports_no_metered_window()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Copilot", "copilot-unlimited.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync("ghp_test", CancellationToken.None)).Snapshot;

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Equal("Business", snapshot.Identity!.PlanName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_requires_authentication(HttpStatusCode status)
    {
        var (client, _) = ClientWith(status, "{}");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync("ghp_test", CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("gh auth login", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_payload_is_a_parse_error()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, "{ not json");

        var result = await client.FetchAsync("ghp_test", CancellationToken.None);

        Assert.Equal(ProviderErrorKind.Parse, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
    }

    [Fact]
    public async Task Transport_failure_is_a_network_error()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection reset"));
        var client = new CopilotUsageClient(StubHttpHandler.Client(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync("ghp_test", CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHttpHandler(_ => throw new OperationCanceledException(cancellation.Token));
        var client = new CopilotUsageClient(StubHttpHandler.Client(handler), () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync("ghp_test", cancellation.Token));
    }
}

public sealed class CopilotProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = new CopilotProvider(
            new CopilotTokenSource(() => new Dictionary<string, string>(), _ => Task.FromResult<string?>(null)),
            new CopilotUsageClient(StubHttpHandler.Client(new StubHttpHandler(HttpStatusCode.OK, "{}")), () => Now));

        Assert.Equal("copilot", provider.Descriptor.Key.Value);
        Assert.Equal("Copilot", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task Missing_token_fails_before_any_request()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "{}");
        var provider = new CopilotProvider(
            new CopilotTokenSource(() => new Dictionary<string, string>(), _ => Task.FromResult<string?>(null)),
            new CopilotUsageClient(StubHttpHandler.Client(handler), () => Now));

        var result = await provider.FetchAsync(CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Contains("gh auth login", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Signed_in_user_gets_a_mapped_snapshot()
    {
        var provider = new CopilotProvider(
            new CopilotTokenSource(() => new Dictionary<string, string> { ["GITHUB_TOKEN"] = "ghp_test" }),
            new CopilotUsageClient(
                StubHttpHandler.Client(new StubHttpHandler(
                    HttpStatusCode.OK,
                    Fixture.Read(Path.Combine("Copilot", "copilot-metered.json")))),
                () => Now));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(58d, snapshot.Primary!.UsedPercent, precision: 6);
    }
}
