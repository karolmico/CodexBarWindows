using System.IO;
using System.Net;
using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.OpenCode;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.OpenCode;

public sealed class OpenCodeProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describes_itself_with_the_upstream_provider_key()
    {
        var provider = new OpenCodeProvider(Resolver(OpenCodeCredentialLookup.None), Client());

        Assert.Equal("opencode", provider.Descriptor.Key.Value);
        Assert.Equal("OpenCode", provider.Descriptor.DisplayName);
    }

    [Fact]
    public async Task No_credential_asks_for_a_sign_in_or_a_key()
    {
        var provider = new OpenCodeProvider(Resolver(OpenCodeCredentialLookup.None), Client());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.NotConfigured, error.Kind);
        Assert.Contains("opencode auth login", error.Message, StringComparison.Ordinal);
        Assert.Contains("API key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_sign_in_error_is_reported_unchanged()
    {
        var provider = new OpenCodeProvider(
            Resolver(OpenCodeCredentialLookup.Failed(
                ProviderError.AuthenticationRequired("The OpenCode sign-in has expired."))),
            Client());

        var error = Assert.IsType<UsageFetchResult.Failure>(
            await provider.FetchAsync(CancellationToken.None)).Error;

        Assert.Equal(ProviderErrorKind.AuthenticationRequired, error.Kind);
    }

    [Fact]
    public async Task A_found_credential_is_used_for_the_request()
    {
        var handler = new StubHttpHandler(
            HttpStatusCode.OK,
            Fixture.Read(Path.Combine("OpenCode", "usage.json")));
        var provider = new OpenCodeProvider(
            Resolver(OpenCodeCredentialLookup.Found(new OpenCodeCredential("cli-token", OpenCodeCredentialKind.ApiKey))),
            new OpenCodeUsageClient(StubHttpHandler.Client(handler), () => Now));

        var snapshot = Assert.IsType<UsageFetchResult.Success>(
            await provider.FetchAsync(CancellationToken.None)).Snapshot;

        Assert.Equal(16d, snapshot.Secondary!.UsedPercent);
        Assert.Equal("Bearer cli-token", handler.LastRequest!.Headers.Authorization!.ToString());
    }

    private static OpenCodeUsageClient Client() =>
        new(
            StubHttpHandler.Client(new StubHttpHandler(
                HttpStatusCode.OK,
                Fixture.Read(Path.Combine("OpenCode", "usage.json")))),
            () => Now);

    private static OpenCodeCredentialResolver Resolver(OpenCodeCredentialLookup lookup) =>
        new(new FakeSource(lookup), new FakeTokenStore(), () => new Dictionary<string, string>());

    private sealed class FakeSource(OpenCodeCredentialLookup lookup) : IOpenCodeCredentialSource
    {
        public Task<OpenCodeCredentialLookup> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(lookup);
    }

    private sealed class FakeTokenStore : IProviderTokenStore
    {
        public string? GetToken(ProviderKey provider) => null;

        public void SaveToken(ProviderKey provider, string token)
        {
        }

        public void ClearToken(ProviderKey provider)
        {
        }
    }
}
