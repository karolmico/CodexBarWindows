using System.Net;
using System.Net.Http;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Claude;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Claude;

public sealed class ClaudeUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly ClaudeCredentials Credentials = new("test-claude-token");

    private static (ClaudeUsageClient Client, StubHttpHandler Handler) ClientWith(
        HttpStatusCode statusCode,
        string body)
    {
        var handler = new StubHttpHandler(statusCode, body);
        return (new ClaudeUsageClient(StubHttpHandler.Client(handler), () => Now), handler);
    }

    [Fact]
    public async Task Calls_the_oauth_usage_endpoint_with_the_required_beta_header()
    {
        var (client, handler) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Claude", "claude-healthy.json")));

        await client.FetchAsync(Credentials, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("api.anthropic.com", request.RequestUri!.Host);
        Assert.Equal("/api/oauth/usage", request.RequestUri.AbsolutePath);
        Assert.Contains("cedar_ember=1", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer test-claude-token", request.Headers.Authorization!.ToString());
        Assert.Equal("oauth-2025-04-20", request.Headers.GetValues("anthropic-beta").Single());
    }

    [Fact]
    public async Task Maps_session_weekly_and_scoped_windows()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Claude", "claude-healthy.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.Equal("claude", snapshot.Provider.Value);
        Assert.Equal(42.5d, snapshot.Primary!.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.Primary.Window);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T15:00:00Z"), snapshot.Primary.ResetsAt);
        Assert.Equal(63d, snapshot.Secondary!.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), snapshot.Secondary.Window);

        Assert.Equal(2, snapshot.ExtraWindows.Count);
        Assert.Equal("Opus", snapshot.ExtraWindows[0].Title);
        Assert.Equal(88d, snapshot.ExtraWindows[0].Window.UsedPercent);
        Assert.Equal("Sonnet", snapshot.ExtraWindows[1].Title);
        Assert.Equal(12d, snapshot.ExtraWindows[1].Window.UsedPercent);
    }

    [Fact]
    public async Task Weekly_only_payload_leaves_the_session_window_absent()
    {
        var (client, _) = ClientWith(
            HttpStatusCode.OK,
            Fixture.Read(Path.Combine("Claude", "claude-weekly-only.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.Null(snapshot.Primary);
        Assert.Equal(63d, snapshot.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task Exhausted_session_is_flagged_and_unmeasured_scopes_are_skipped()
    {
        var (client, _) = ClientWith(
            HttpStatusCode.OK,
            Fixture.Read(Path.Combine("Claude", "claude-exhausted-with-unmeasured-scope.json")));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Snapshot;

        Assert.True(snapshot.IsExhausted);
        Assert.Empty(snapshot.ExtraWindows);
    }

    [Fact]
    public async Task Malformed_payload_is_a_parse_error()
    {
        var (client, _) = ClientWith(HttpStatusCode.OK, Fixture.Read(Path.Combine("Claude", "claude-malformed.json")));

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        Assert.Equal(ProviderErrorKind.Parse, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
    }

    [Fact]
    public async Task Rejected_token_requires_authentication()
    {
        var (client, _) = ClientWith(HttpStatusCode.Unauthorized, "{}");

        var result = await client.FetchAsync(Credentials, CancellationToken.None);

        var error = Assert.IsType<UsageFetchResult.Failure>(result).Error;
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("claude", error.Message, StringComparison.OrdinalIgnoreCase);
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
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        var client = new ClaudeUsageClient(StubHttpHandler.Client(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(90), error.RetryAfter);
    }

    [Fact]
    public async Task Server_error_is_a_network_error()
    {
        var (client, _) = ClientWith(HttpStatusCode.InternalServerError, "{}");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Transport_failure_is_a_network_error()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection reset"));
        var client = new ClaudeUsageClient(StubHttpHandler.Client(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credentials, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHttpHandler(_ => throw new OperationCanceledException(cancellation.Token));
        var client = new ClaudeUsageClient(StubHttpHandler.Client(handler), () => Now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(Credentials, cancellation.Token));
    }
}
