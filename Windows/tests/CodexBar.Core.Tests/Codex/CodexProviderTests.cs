using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Codex;

public sealed class CodexProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = Provider(_ => null, new StubHttpHandler(HttpStatusCode.OK, "{}"));

        Assert.Equal("codex", provider.Descriptor.Key.Value);
        Assert.Equal("Codex", provider.Descriptor.DisplayName);
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
            _ => Fixture.Read(Path.Combine("Codex", "auth-healthy.json")),
            new StubHttpHandler(HttpStatusCode.OK, Fixture.Read(Path.Combine("Codex", "codex-healthy.json"))));

        var result = await provider.FetchAsync(CancellationToken.None);

        var snapshot = Assert.IsType<UsageFetchResult.Success>(result).Snapshot;
        Assert.Equal(42d, snapshot.Primary!.UsedPercent);
        Assert.Equal("Plus", snapshot.Identity!.PlanName);
        Assert.Equal(Now, snapshot.UpdatedAt);
    }

    private static CodexProvider Provider(Func<string, string?> readFile, StubHttpHandler handler) =>
        new(
            new CodexAuthFileStore(
                () => new Dictionary<string, string>(),
                @"C:\Users\Test",
                readFile),
            new CodexUsageClient(StubHttpHandler.Client(handler), () => Now));
}
