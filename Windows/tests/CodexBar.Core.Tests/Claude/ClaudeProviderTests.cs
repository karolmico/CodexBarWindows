using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Claude;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Claude;

public sealed class ClaudeProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = Provider(_ => null, new StubHttpHandler(HttpStatusCode.OK, "{}"));

        Assert.Equal("claude", provider.Descriptor.Key.Value);
        Assert.Equal("Claude", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task Missing_credentials_fail_without_calling_the_network()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "{}");
        var provider = Provider(_ => null, handler);

        var result = await provider.FetchAsync(CancellationToken.None);

        Assert.Equal(ProviderErrorKind.NotConfigured, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Signed_in_user_gets_a_mapped_snapshot()
    {
        var provider = Provider(
            _ => Fixture.Read(Path.Combine("Claude", "auth-healthy.json")),
            new StubHttpHandler(HttpStatusCode.OK, Fixture.Read(Path.Combine("Claude", "claude-healthy.json"))));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(42.5d, snapshot.Primary!.UsedPercent);
        Assert.Equal(Now, snapshot.UpdatedAt);
    }

    private static ClaudeProvider Provider(Func<string, string?> readFile, StubHttpHandler handler) =>
        new(
            new ClaudeAuthFileStore(
                () => new Dictionary<string, string>(),
                @"C:\Users\Test",
                readFile,
                () => Now),
            new ClaudeUsageClient(StubHttpHandler.Client(handler), () => Now));
}
