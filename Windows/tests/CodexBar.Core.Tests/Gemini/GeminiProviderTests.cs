using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Gemini;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Gemini;

public sealed class GeminiProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = new GeminiProvider(
            new GeminiAuthFileStore(@"C:\Users\Test", _ => null, () => Now),
            new GeminiUsageClient(new HttpClient(), () => Now));

        Assert.Equal("gemini", provider.Descriptor.Key.Value);
        Assert.Equal("Gemini", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task Missing_credentials_fail_before_any_request()
    {
        var handler = new RoutingHandler([]);
        var provider = new GeminiProvider(
            new GeminiAuthFileStore(@"C:\Users\Test", _ => null, () => Now),
            new GeminiUsageClient(new HttpClient(handler, disposeHandler: false), () => Now));

        var result = await provider.FetchAsync(CancellationToken.None);

        Assert.Equal(ProviderErrorKind.NotConfigured, Assert.IsType<UsageFetchResult.Failure>(result).Error.Kind);
        Assert.Empty(handler.Requests);
    }
}
