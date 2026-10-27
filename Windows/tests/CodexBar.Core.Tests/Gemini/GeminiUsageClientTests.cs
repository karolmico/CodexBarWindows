using System.Net;
using System.Net.Http;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Gemini;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Gemini;

public sealed class GeminiUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeminiCredentials Credentials =
        new("test-gemini-token", IdToken: "eyJhbGciOiJub25lIn0.eyJlbWFpbCI6InVzZXJAZXhhbXBsZS5jb20ifQ.");

    private static (GeminiUsageClient Client, RoutingHandler Handler) ClientWith(string quotaBody = "quota.json")
    {
        var handler = new RoutingHandler([])
            .Add("/v1internal:loadCodeAssist", HttpStatusCode.OK, Fixture.Read(Path.Combine("Gemini", "code-assist.json")))
            .Add("/v1internal:retrieveUserQuota", HttpStatusCode.OK, Fixture.Read(Path.Combine("Gemini", quotaBody)));
        return (new GeminiUsageClient(Stub(handler), () => Now), handler);
    }

    [Fact]
    public async Task Resolves_the_project_then_fetches_quota_with_bearer_auth()
    {
        var (client, handler) = ClientWith();

        await client.FetchAsync(Credentials, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var status = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, status.Method);
        Assert.Equal("/v1internal:loadCodeAssist", status.Uri.AbsolutePath);
        Assert.Equal("Bearer test-gemini-token", status.Authorization);
        Assert.Contains("GEMINI_CLI", status.Body);

        var quota = handler.Requests[1];
        Assert.Equal("/v1internal:retrieveUserQuota", quota.Uri.AbsolutePath);
        Assert.Contains("projects/123", quota.Body);
    }

    [Fact]
    public async Task Maps_pro_flash_and_flash_lite_lanes()
    {
        var (client, _) = ClientWith();

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.Equal("gemini", snapshot.Provider.Value);
        Assert.Equal(42d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromMinutes(1440), snapshot.Primary.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-10-28T00:00:00Z"), snapshot.Primary.ResetsAt);
        Assert.Equal(63d, snapshot.Secondary!.UsedPercent, precision: 6);

        var flashLite = Assert.Single(snapshot.ExtraWindows);
        Assert.Equal("Flash Lite", flashLite.Title);
        Assert.Equal(88d, flashLite.Window.UsedPercent, precision: 6);

        Assert.Equal("Gemini Code Assist", snapshot.Identity!.PlanName);
        Assert.Equal("user@example.com", snapshot.Identity.AccountLabel);
    }

    [Fact]
    public async Task Empty_bucket_list_reports_no_quota_instead_of_zero_percent()
    {
        var (client, _) = ClientWith("quota-empty.json");

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Null(snapshot.Primary);
        Assert.Equal("Gemini Code Assist", snapshot.Identity!.PlanName);
    }

    [Fact]
    public async Task Rejected_token_requires_authentication()
    {
        var handler = new RoutingHandler([])
            .Add("/v1internal:loadCodeAssist", HttpStatusCode.OK, Fixture.Read(Path.Combine("Gemini", "code-assist.json")))
            .Add("/v1internal:retrieveUserQuota", HttpStatusCode.Unauthorized, "{}");
        var client = new GeminiUsageClient(Stub(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("gemini", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rate_limit_response_preserves_retry_after()
    {
        var handler = new RoutingHandler([])
            .Add("/v1internal:loadCodeAssist", HttpStatusCode.OK, "{}")
            .Add("/v1internal:retrieveUserQuota", HttpStatusCode.TooManyRequests, "{}");
        var client = new GeminiUsageClient(Stub(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
    }

    [Fact]
    public async Task Server_error_is_a_network_error()
    {
        var handler = new RoutingHandler([])
            .Add("/v1internal:loadCodeAssist", HttpStatusCode.OK, "{}")
            .Add("/v1internal:retrieveUserQuota", HttpStatusCode.InternalServerError, "{}");
        var client = new GeminiUsageClient(Stub(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new RoutingHandler([])
            .Add("/v1internal:loadCodeAssist", HttpStatusCode.OK, "{}")
            .Add("/v1internal:retrieveUserQuota", HttpStatusCode.OK, "{}");
        var client = new GeminiUsageClient(
            new HttpClient(new ThrowingHandler(cancellation.Token), disposeHandler: false),
            () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(Credentials, cancellation.Token));
    }

    private static HttpClient Stub(RoutingHandler handler) => new(handler, disposeHandler: false);

    private sealed class ThrowingHandler(CancellationToken token) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException(token));
    }
}
