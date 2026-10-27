using System.Net;
using System.Net.Http;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Codex;

public sealed class CodexUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly CodexCredentials Credentials =
        new("test-access-token", "acct_123", RefreshToken: null, IdToken: null, IsApiKey: false);

    private static (CodexUsageClient Client, StubHttpHandler Handler) ClientWith(
        HttpStatusCode statusCode,
        string body)
    {
        var handler = new StubHttpHandler(statusCode, body);
        return (new CodexUsageClient(StubHttpHandler.Client(handler), () => Now), handler);
    }

    [Fact]
    public async Task Calls_the_chatgpt_usage_endpoint_with_bearer_and_account_headers()
    {
        var (client, handler) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-healthy.json")));

        await client.FetchAsync(Credentials, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("chatgpt.com", request.RequestUri!.Host);
        Assert.Equal("/backend-api/wham/usage", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer test-access-token", request.Headers.Authorization!.ToString());
        Assert.Equal("acct_123", request.Headers.GetValues("ChatGPT-Account-Id").Single());
        Assert.StartsWith("CodexBar", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Omits_the_account_header_when_the_credential_has_no_account_id()
    {
        var (client, handler) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-healthy.json")));

        await client.FetchAsync(Credentials with { AccountId = null }, CancellationToken.None);

        Assert.False(handler.LastRequest!.Headers.Contains("ChatGPT-Account-Id"));
    }

    [Fact]
    public async Task Maps_a_healthy_payload_into_session_and_weekly_windows()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-healthy.json")));

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(result).Snapshot;
        Assert.Equal("codex", snapshot.Provider.Value);
        Assert.Equal(Now, snapshot.UpdatedAt);
        Assert.Equal("Plus", snapshot.Identity!.PlanName);

        var session = snapshot.Primary!;
        Assert.Equal(42d, session.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), session.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T12:00:00Z"), session.ResetsAt);

        var weekly = snapshot.Secondary!;
        Assert.Equal(87d, weekly.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), weekly.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T11:00:00Z"), weekly.ResetsAt);
    }

    [Fact]
    public async Task Maps_an_exhausted_payload_and_keeps_fractional_percentages()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-exhausted.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.True(snapshot.IsExhausted);
        Assert.Equal(100d, snapshot.Primary!.UsedPercent);
        Assert.Equal(55.5d, snapshot.Secondary!.UsedPercent);
        Assert.Equal("Pro", snapshot.Identity!.PlanName);
    }

    [Fact]
    public async Task Weekly_only_payload_leaves_the_session_window_absent()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-weekly-only.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.Null(snapshot.Primary);
        Assert.Equal(63d, snapshot.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task Payload_without_rate_limits_is_a_snapshot_without_windows()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, """{"account_id":"acct_123","plan_type":"free"}""");

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.False(snapshot.HasMeasuredWindows);
        Assert.Equal("Free", snapshot.Identity!.PlanName);
    }

    [Fact]
    public async Task Malformed_payload_is_a_parse_error()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-malformed.json")));

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        Assert.Equal(ProviderErrorKind.Parse, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_token_requires_authentication_with_sign_in_guidance(HttpStatusCode statusCode)
    {
        var (client, _) = ClientWith(statusCode, """{"error":"unauthorized"}""");

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("codex login", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_response_preserves_retry_after()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });
        var client = new CodexUsageClient(StubHttpHandler.Client(handler), () => Now);

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(120), error.RetryAfter);
    }

    [Fact]
    public async Task Server_error_is_a_network_error()
    {
        var (client, _) = ClientWith(HttpStatusCode.InternalServerError, "{}");

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.Network, error.Kind);
        Assert.Contains("500", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_failure_is_a_network_error()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection reset"));
        var client = new CodexUsageClient(StubHttpHandler.Client(handler), () => Now);

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        Assert.Equal(ProviderErrorKind.Network, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_an_error_value()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHttpHandler(_ => throw new OperationCanceledException(cancellation.Token));
        var client = new CodexUsageClient(StubHttpHandler.Client(handler), () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(Credentials, cancellation.Token));
    }
}
