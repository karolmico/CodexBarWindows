using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Codex;

public sealed class CodexAuthFileStoreTests
{
    private const string UserProfile = @"C:\Users\Test";
    private static readonly string DefaultAuthPath = Path.Combine(UserProfile, ".codex", "auth.json");

    [Fact]
    public void Reads_auth_json_from_the_codex_home_override()
    {
        var readPath = string.Empty;
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string> { ["CODEX_HOME"] = @"D:\custom-codex" },
            UserProfile,
            path =>
            {
                readPath = path;
                return Fixture.Read(Path.Combine("Codex", "auth-healthy.json"));
            });

        var result = store.Load();

        Assert.NotNull(result.Credentials);
        Assert.Equal(Path.Combine(@"D:\custom-codex", "auth.json"), readPath);
        Assert.Equal("test-access-token", result.Credentials!.AccessToken);
        Assert.Equal("acct_123", result.Credentials.AccountId);
        Assert.Equal("test-refresh-token", result.Credentials.RefreshToken);
        Assert.False(result.Credentials.IsApiKey);
    }

    [Fact]
    public void Falls_back_to_the_per_user_codex_home()
    {
        var readPath = string.Empty;
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            path =>
            {
                readPath = path;
                return Fixture.Read(Path.Combine("Codex", "auth-healthy.json"));
            });

        store.Load();

        Assert.Equal(DefaultAuthPath, readPath);
    }

    [Fact]
    public void Accepts_an_api_key_auth_file()
    {
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => """{"OPENAI_API_KEY":"sk-test-key"}""");

        var result = store.Load();

        Assert.True(result.Credentials!.IsApiKey);
        Assert.Equal("sk-test-key", result.Credentials.AccessToken);
        Assert.Null(result.Credentials.AccountId);
    }

    [Fact]
    public void Missing_auth_file_points_at_the_sign_in_command()
    {
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => null);

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.NotConfigured, result.Error!.Kind);
        Assert.Contains("codex login", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_file_without_tokens_requires_authentication()
    {
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => Fixture.Read(Path.Combine("Codex", "auth-missing-tokens.json")));

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
    }

    [Fact]
    public void Unreadable_auth_file_is_a_parse_error_with_guidance()
    {
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => "{not json");

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.Parse, result.Error!.Kind);
        Assert.Contains("codex login", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_codex_home_is_picked_up_on_the_next_load()
    {
        var environment = new Dictionary<string, string>();
        var paths = new List<string>();
        var store = new CodexAuthFileStore(
            () => environment,
            UserProfile,
            path =>
            {
                paths.Add(path);
                return Fixture.Read(Path.Combine("Codex", "auth-healthy.json"));
            });

        store.Load();
        environment["CODEX_HOME"] = @"E:\other-codex";
        store.Load();

        Assert.Equal(DefaultAuthPath, paths[0]);
        Assert.Equal(Path.Combine(@"E:\other-codex", "auth.json"), paths[1]);
    }

    [Fact]
    public void Credential_text_never_contains_the_token()
    {
        var store = new CodexAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => Fixture.Read(Path.Combine("Codex", "auth-healthy.json")));

        var text = store.Load().Credentials!.ToString();

        Assert.DoesNotContain("test-access-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test-refresh-token", text, StringComparison.Ordinal);
    }
}
