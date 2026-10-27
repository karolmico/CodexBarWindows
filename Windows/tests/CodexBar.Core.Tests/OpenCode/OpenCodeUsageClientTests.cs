using System.IO;
using System.Net;
using System.Net.Http;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.OpenCode;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.OpenCode;

public sealed class OpenCodeUsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly OpenCodeCredential Credential = new("sk-test-value", OpenCodeCredentialKind.ApiKey);

    private static string Report(string name) => Fixture.Read(Path.Combine("OpenCode", name));

    private static OpenCodeUsageClient Client(StubHttpHandler handler) =>
        new(StubHttpHandler.Client(handler), () => Now);

    [Fact]
    public async Task Healthy_report_maps_all_three_windows()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Report("usage.json"));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Snapshot;

        Assert.Equal("opencode", snapshot.Provider.Value);
        Assert.Equal(0d, snapshot.Primary!.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.Primary.Window);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 20, 25, 2, TimeSpan.Zero), snapshot.Primary.ResetsAt);
        Assert.Equal(16d, snapshot.Secondary!.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), snapshot.Secondary.Window);

        var monthly = Assert.Single(snapshot.ExtraWindows);
        Assert.Equal("opencode-monthly", monthly.Id);
        Assert.Equal("OpenCode monthly", monthly.Title);
        Assert.Equal(33d, monthly.Window.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(30), monthly.Window.Window);
    }

    [Fact]
    public async Task Request_uses_a_bearer_token_and_the_usage_endpoint()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Report("usage.json"));

        await Client(handler).FetchAsync(Credential, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal(OpenCodeUsageClient.UsageEndpoint, handler.LastRequest.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test-value", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Legacy_field_names_and_relative_resets_are_still_read()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Report("usage-legacy.json"));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Snapshot;

        Assert.Equal(12.5d, snapshot.Primary!.UsedPercent);
        Assert.Equal(Now.AddHours(1), snapshot.Primary.ResetsAt);
        Assert.Equal(42d, snapshot.Secondary!.UsedPercent);
        Assert.Equal(Now.AddDays(2), snapshot.Secondary.ResetsAt);
        Assert.Empty(snapshot.ExtraWindows);
    }

    [Fact]
    public async Task A_report_without_rolling_usage_is_a_parse_error()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Report("usage-no-rolling.json"));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Unreadable_json_is_a_parse_error()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "{ \"usage\": ");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Parse, error.Kind);
    }

    [Fact]
    public async Task Rejected_credential_asks_for_a_sign_in()
    {
        var handler = new StubHttpHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid token\"}");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Contains("opencode auth login", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test-value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limiting_keeps_the_retry_after_hint()
    {
        var handler = new StubHttpHandler((HttpStatusCode)429, "{}");
        var client = new OpenCodeUsageClient(StubHttpHandler.Client(handler), () => Now);

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await client.FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.RateLimited, error.Kind);
    }

    [Fact]
    public async Task Server_errors_are_network_errors()
    {
        var handler = new StubHttpHandler(HttpStatusCode.BadGateway, "{}");

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task Transport_failures_are_network_errors()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("no route"));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(Credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task An_expired_oauth_credential_is_refused_without_a_request()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, Report("usage.json"));
        var credential = new OpenCodeCredential(
            "oauth-value",
            OpenCodeCredentialKind.OAuth,
            Now.AddMinutes(-1));

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await Client(handler).FetchAsync(credential, CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void Percentages_are_clamped_to_the_supported_range()
    {
        var result = OpenCodeUsageClient.ParseUsage(
            "{\"usage\":{\"rolling\":{\"percent\":150},\"weekly\":{\"percent\":-4}}}",
            Now);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(result).Snapshot;
        Assert.Equal(100d, snapshot.Primary!.UsedPercent);
        Assert.Equal(0d, snapshot.Secondary!.UsedPercent);
    }


}
