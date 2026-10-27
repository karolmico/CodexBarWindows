using System.Net;
using System.Text.Json;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Cursor;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Cursor;

public sealed class CursorUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Token = Jwt(Now.AddDays(30));

    private static CursorSession Session() =>
        new(Token, "user_01TEST", "user@example.com", Now.AddDays(30));

    private static (CursorUsageClient Client, RoutingHandler Handler) ClientWith(
        string summaryFixture = "usage-summary.json",
        bool withProfile = true,
        bool withLegacy = false,
        HttpStatusCode summaryStatus = HttpStatusCode.OK,
        string? summaryBody = null,
        TimeSpan? retryAfter = null)
    {
        var handler = new RoutingHandler([]);
        handler.Add(
            "/api/usage-summary",
            summaryStatus,
            summaryBody ?? Fixture.Read(Path.Combine("Cursor", summaryFixture)),
            retryAfter);
        if (withProfile)
        {
            handler.Add(
                "/api/auth/me",
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("Cursor", "auth-me.json")));
        }

        if (withLegacy)
        {
            handler.Add(
                "/api/usage",
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("Cursor", "usage-legacy.json")));
        }

        return (new CursorUsageClient(new HttpClient(handler, disposeHandler: false), () => Now), handler);
    }

    [Fact]
    public async Task Calls_usage_summary_with_the_session_cookie()
    {
        var (client, handler) = ClientWith();

        await client.FetchAsync(Session(), CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal("cursor.com", request.Uri.Host);
        Assert.Equal("/api/usage-summary", request.Uri.AbsolutePath);
        Assert.NotNull(request.Cookie);
        Assert.StartsWith("WorkosCursorSessionToken=user_01TEST%3A%3A", request.Cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp", request.Cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_plan_auto_and_api_lanes()
    {
        var (client, _) = ClientWith();

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Session(), CancellationToken.None)).Snapshot;

        Assert.Equal("cursor", snapshot.Provider.Value);
        Assert.Equal(72.5d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromDays(31), snapshot.Primary.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:00:00Z"), snapshot.Primary.ResetsAt);
        Assert.Equal(68.5d, snapshot.Secondary!.UsedPercent, precision: 6);

        var api = Assert.Single(snapshot.ExtraWindows);
        Assert.Equal("API", api.Title);
        Assert.Equal(12.25d, api.Window.UsedPercent, precision: 6);

        Assert.Equal("Cursor Pro", snapshot.Identity!.PlanName);
        Assert.Equal("user@example.com", snapshot.Identity.AccountLabel);
    }

    [Fact]
    public async Task Legacy_request_plan_uses_the_request_quota()
    {
        var (client, handler) = ClientWith("usage-summary-legacy.json", withLegacy: true);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Session(), CancellationToken.None)).Snapshot;

        Assert.Equal(29d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal(500d, snapshot.Primary.QuotaLimit);
        Assert.Equal("requests", snapshot.Primary.QuotaUnit);
        Assert.Null(snapshot.Secondary);
        Assert.Equal("Cursor Free", snapshot.Identity!.PlanName);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/api/usage"
                && request.Uri.Query.Contains("user_01TEST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejected_session_requires_authentication()
    {
        var (client, _) = ClientWith(
            summaryStatus: HttpStatusCode.Unauthorized,
            summaryBody: "{}",
            withProfile: false);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Session(), CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("Cursor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_response_preserves_retry_after()
    {
        var (client, _) = ClientWith(
            summaryStatus: HttpStatusCode.TooManyRequests,
            summaryBody: "{}",
            withProfile: false,
            retryAfter: TimeSpan.FromSeconds(45));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Session(), CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(45), error.RetryAfter);
    }

    [Fact]
    public async Task Server_error_is_a_network_error()
    {
        var (client, _) = ClientWith(
            summaryStatus: HttpStatusCode.InternalServerError,
            summaryBody: "{}",
            withProfile: false);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Session(), CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Malformed_payload_is_a_parse_error()
    {
        var (client, _) = ClientWith(summaryBody: "{ not json", withProfile: false);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Session(), CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Payload_without_any_quota_reports_no_measured_window()
    {
        var (client, _) = ClientWith(
            summaryBody: "{\"membershipType\":\"pro\",\"individualUsage\":{}}",
            withProfile: false);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Session(), CancellationToken.None)).Snapshot;

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Equal("Cursor Pro", snapshot.Identity!.PlanName);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new CursorUsageClient(
            new HttpClient(new CancelHandler(), disposeHandler: false),
            () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(Session(), cancellation.Token));
    }

    private static string Jwt(DateTimeOffset expiresAt)
    {
        static string Part(string value) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var payload = JsonSerializer.Serialize(new
        {
            sub = "auth0|user_01TEST",
            email = "user@example.com",
            exp = expiresAt.ToUnixTimeSeconds(),
        });
        return $"{Part("{\"alg\":\"none\"}")}.{Part(payload)}.";
    }

    private sealed class CancelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new OperationCanceledException(cancellationToken));
    }
}
