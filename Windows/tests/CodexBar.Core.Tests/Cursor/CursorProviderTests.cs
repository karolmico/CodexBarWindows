using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Cursor;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Cursor;

public sealed class CursorProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = new CursorProvider(
            new CursorSessionStore(@"C:\Temp", @"C:\Users\Test", _ => null, _ => null, () => Now),
            new CursorUsageClient(new HttpClient(), () => Now));

        Assert.Equal("cursor", provider.Descriptor.Key.Value);
        Assert.Equal("Cursor", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task Missing_session_fails_before_any_request()
    {
        var handler = new RoutingHandler([]);
        var provider = new CursorProvider(
            new CursorSessionStore(@"C:\Temp", @"C:\Users\Test", _ => null, _ => null, () => Now),
            new CursorUsageClient(new HttpClient(handler, disposeHandler: false), () => Now));

        var result = await provider.FetchAsync(CancellationToken.None);

        Assert.Equal(ProviderErrorKind.NotConfigured, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Signed_in_user_gets_a_mapped_snapshot()
    {
        var token = Jwt(Now.AddDays(30));
        var handler = new RoutingHandler([])
            .Add("/api/usage-summary", HttpStatusCode.OK, Fixture.Read(Path.Combine("Cursor", "usage-summary.json")))
            .Add("/api/auth/me", HttpStatusCode.OK, Fixture.Read(Path.Combine("Cursor", "auth-me.json")));
        var provider = new CursorProvider(
            new CursorSessionStore(@"C:\Temp", @"C:\Users\Test", _ => null, _ => token, () => Now),
            new CursorUsageClient(new HttpClient(handler, disposeHandler: false), () => Now));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(72.5d, snapshot.Primary!.UsedPercent, precision: 6);
        Assert.Equal("Cursor Pro", snapshot.Identity!.PlanName);
    }

    private static string Jwt(DateTimeOffset expiresAt)
    {
        static string Part(string value) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            sub = "auth0|user_01TEST",
            exp = expiresAt.ToUnixTimeSeconds(),
        });
        return $"{Part("{\"alg\":\"none\"}")}.{Part(payload)}.";
    }
}
