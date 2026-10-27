using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.OpenCode;
using Xunit;

namespace CodexBar.Core.Tests.OpenCode;

public sealed class OpenCodeCredentialResolverTests
{
    [Fact]
    public async Task A_saved_key_wins_over_the_environment_and_the_cli()
    {
        var resolver = Resolver(
            stored: "sk-saved",
            environment: new Dictionary<string, string> { ["OPENCODE_API_KEY"] = "sk-environment" },
            cli: OpenCodeCredentialLookup.Found(new OpenCodeCredential("cli-token", OpenCodeCredentialKind.ApiKey)));

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("sk-saved", lookup.Credential!.Token);
    }

    [Fact]
    public async Task The_environment_key_wins_over_the_cli_login()
    {
        var resolver = Resolver(
            stored: null,
            environment: new Dictionary<string, string> { ["OPENCODE_API_KEY"] = "sk-environment" },
            cli: OpenCodeCredentialLookup.Found(new OpenCodeCredential("cli-token", OpenCodeCredentialKind.ApiKey)));

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("sk-environment", lookup.Credential!.Token);
    }

    [Fact]
    public async Task The_cli_login_is_used_when_nothing_else_is_saved()
    {
        var resolver = Resolver(
            stored: null,
            environment: new Dictionary<string, string>(),
            cli: OpenCodeCredentialLookup.Found(new OpenCodeCredential(
                "cli-token",
                OpenCodeCredentialKind.OAuth,
                DateTimeOffset.UtcNow.AddDays(1))));

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("cli-token", lookup.Credential!.Token);
        Assert.Equal(OpenCodeCredentialKind.OAuth, lookup.Credential.Kind);
    }

    [Fact]
    public async Task A_cli_error_is_preserved_instead_of_becoming_no_credential()
    {
        var resolver = Resolver(
            stored: null,
            environment: new Dictionary<string, string>(),
            cli: OpenCodeCredentialLookup.Failed(
                ProviderError.AuthenticationRequired("The OpenCode sign-in has expired.")));

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, lookup.Error!.Kind);
    }

    [Fact]
    public async Task Whitespace_and_quoted_keys_are_normalised()
    {
        var resolver = Resolver(
            stored: "  \"sk-quoted\"  ",
            environment: new Dictionary<string, string>(),
            cli: OpenCodeCredentialLookup.None);

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("sk-quoted", lookup.Credential!.Token);
    }

    [Fact]
    public async Task A_blank_stored_value_falls_through_to_the_cli()
    {
        var resolver = Resolver(
            stored: "   ",
            environment: new Dictionary<string, string>(),
            cli: OpenCodeCredentialLookup.Found(new OpenCodeCredential("cli-token", OpenCodeCredentialKind.ApiKey)));

        var lookup = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("cli-token", lookup.Credential!.Token);
    }

    [Fact]
    public void A_token_with_embedded_whitespace_is_rejected()
    {
        Assert.Null(OpenCodeCredential.Normalise("sk-two words"));
        Assert.Null(OpenCodeCredential.Normalise(""));
        Assert.Equal("sk-key", OpenCodeCredential.Normalise(" 'sk-key' "));
    }

    private static OpenCodeCredentialResolver Resolver(
        string? stored,
        IReadOnlyDictionary<string, string> environment,
        OpenCodeCredentialLookup cli) =>
        new(new FakeCredentialSource(cli), new FakeTokenStore(stored), () => environment);

    private sealed class FakeCredentialSource(OpenCodeCredentialLookup lookup) : IOpenCodeCredentialSource
    {
        public Task<OpenCodeCredentialLookup> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(lookup);
    }

    private sealed class FakeTokenStore(string? token) : IProviderTokenStore
    {
        public string? GetToken(ProviderKey provider) => token;

        public void SaveToken(ProviderKey provider, string value)
        {
        }

        public void ClearToken(ProviderKey provider)
        {
        }
    }
}
