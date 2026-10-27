using System.Diagnostics;
using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Antigravity;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Antigravity;

public sealed class AntigravityUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly AntigravityEndpoint Endpoint =
        new(4242, AntigravityProcessKind.App, "token-123", 51234);

    private static (AntigravityUsageClient Client, RoutingHandler Handler) ClientWith(
        string quotaFixture = "quota-summary.json",
        HttpStatusCode quotaStatus = HttpStatusCode.OK)
    {
        var handler = new RoutingHandler([])
            .Add(
                AntigravityUsageClient.QuotaSummaryPath,
                quotaStatus,
                Fixture.Read(Path.Combine("Antigravity", quotaFixture)))
            .Add(
                AntigravityUsageClient.UserStatusPath,
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("Antigravity", "user-status.json")));
        return (new AntigravityUsageClient(new HttpClient(handler, disposeHandler: false), () => Now), handler);
    }

    [Fact]
    public async Task Posts_the_quota_request_with_the_csrf_token()
    {
        var (client, handler) = ClientWith();

        await client.FetchAsync(Endpoint, [51234], CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("127.0.0.1", request.Uri.Host);
        Assert.Equal(51234, request.Uri.Port);
        Assert.Equal(AntigravityUsageClient.QuotaSummaryPath, request.Uri.AbsolutePath);
        Assert.Equal("token-123", request.CsrfToken);
        Assert.Equal("1", request.ConnectProtocolVersion);
        Assert.Contains("forceRefresh", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_gemini_and_claude_lanes()
    {
        var (client, _) = ClientWith();

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Snapshot;

        Assert.Equal("antigravity", snapshot.Provider.Value);
        Assert.Equal(58d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromMinutes(10080), snapshot.Primary.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T00:00:00Z"), snapshot.Primary.ResetsAt);
        Assert.Equal(40d, snapshot.Secondary!.UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromMinutes(300), snapshot.Secondary.Window);

        var session = Assert.Single(snapshot.ExtraWindows);
        Assert.Equal("Gemini 5-hour", session.Title);
        Assert.Equal(18d, session.Window.UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromMinutes(300), session.Window.Window);

        Assert.Equal("Pro", snapshot.Identity!.PlanName);
        Assert.Equal("user@example.com", snapshot.Identity.AccountLabel);
    }

    [Fact]
    public async Task Empty_bucket_list_is_a_parse_error()
    {
        var (client, _) = ClientWith("quota-summary-empty.json");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
        Assert.Contains("no usable quota", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejected_session_requires_authentication()
    {
        var (client, _) = ClientWith(quotaStatus: HttpStatusCode.Unauthorized);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("Restart Antigravity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_error_is_a_network_error()
    {
        var (client, _) = ClientWith(quotaStatus: HttpStatusCode.InternalServerError);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Malformed_payload_is_a_parse_error()
    {
        var handler = new RoutingHandler([])
            .Add(AntigravityUsageClient.QuotaSummaryPath, HttpStatusCode.OK, "{ not json");
        var client = new AntigravityUsageClient(new HttpClient(handler, disposeHandler: false), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Probe_timeout_is_a_network_error()
    {
        var client = new AntigravityUsageClient(
            new HttpClient(new HangingHandler(), disposeHandler: false),
            () => Now,
            TimeSpan.FromMilliseconds(50));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Endpoint, [51234], CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
        Assert.Contains("in time", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new AntigravityUsageClient(
            new HttpClient(new HangingHandler(), disposeHandler: false),
            () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(Endpoint, [51234], cancellation.Token));
    }

    [Fact]
    public void Candidate_ports_prefer_the_extension_server_then_listeners()
    {
        Assert.Equal(
            new[] { 51234, 6000, 7000 },
            AntigravityUsageClient.CandidatePorts(Endpoint, [6000, 51234, 7000]));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }
}
