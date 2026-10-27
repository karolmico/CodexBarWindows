using System.Net;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Gemini;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Gemini;

public sealed class GeminiAuthFileStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private const string UserProfile = @"C:\Users\Test";

    [Fact]
    public void Reads_oauth_credentials_from_the_gemini_directory()
    {
        var readPath = string.Empty;
        var store = new GeminiAuthFileStore(
            UserProfile,
            path =>
            {
                readPath = path;
                return Fixture.Read(Path.Combine("Gemini", "oauth-healthy.json"));
            },
            () => Now);

        var result = store.Load();

        Assert.Equal(Path.Combine(UserProfile, ".gemini", "oauth_creds.json"), readPath);
        Assert.Equal("test-gemini-token", result.Credentials!.AccessToken);
        Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), result.Credentials.ExpiresAt);
        Assert.NotNull(result.Credentials.IdToken);
    }

    [Fact]
    public void Missing_credentials_point_at_the_gemini_cli()
    {
        var store = new GeminiAuthFileStore(UserProfile, _ => null, () => Now);

        var result = store.Load();

        Assert.Equal(ProviderErrorKind.NotConfigured, result.Error!.Kind);
        Assert.Contains("gemini", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_token_requires_authentication()
    {
        var store = new GeminiAuthFileStore(
            UserProfile,
            _ => Fixture.Read(Path.Combine("Gemini", "oauth-expired.json")),
            () => Now);

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
        Assert.Contains("expired", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_credentials_file_is_a_parse_error()
    {
        var store = new GeminiAuthFileStore(UserProfile, _ => "{ not json", () => Now);

        var result = store.Load();

        Assert.Equal(ProviderErrorKind.Parse, result.Error!.Kind);
    }

    [Fact]
    public void Credential_text_never_contains_the_token()
    {
        var store = new GeminiAuthFileStore(
            UserProfile,
            _ => Fixture.Read(Path.Combine("Gemini", "oauth-healthy.json")),
            () => Now);

        var text = store.Load().Credentials!.ToString();

        Assert.DoesNotContain("test-gemini-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test-gemini-refresh", text, StringComparison.Ordinal);
    }
}
