using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Claude;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests.Claude;

public sealed class ClaudeAuthFileStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private const string UserProfile = @"C:\Users\Test";
    private static readonly string DefaultPath = Path.Combine(UserProfile, ".claude", ".credentials.json");

    [Fact]
    public void Reads_credentials_from_the_claude_config_directory_override()
    {
        var readPath = string.Empty;
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = @"D:\claude-config" },
            UserProfile,
            path =>
            {
                readPath = path;
                return Fixture.Read(Path.Combine("Claude", "auth-healthy.json"));
            },
            () => Now);

        var result = store.Load();

        Assert.Equal(Path.Combine(@"D:\claude-config", ".credentials.json"), readPath);
        Assert.Equal("test-claude-token", result.Credentials!.AccessToken);
        Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), result.Credentials.ExpiresAt);
    }

    [Fact]
    public void Falls_back_to_the_per_user_claude_directory()
    {
        var readPath = string.Empty;
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            path =>
            {
                readPath = path;
                return Fixture.Read(Path.Combine("Claude", "auth-healthy.json"));
            },
            () => Now);

        store.Load();

        Assert.Equal(DefaultPath, readPath);
    }

    [Fact]
    public void Missing_credentials_point_at_the_claude_cli()
    {
        var store = new ClaudeAuthFileStore(() => new Dictionary<string, string>(), UserProfile, _ => null, () => Now);

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.NotConfigured, result.Error!.Kind);
        Assert.Contains("claude", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_token_requires_authentication()
    {
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => Fixture.Read(Path.Combine("Claude", "auth-no-token.json")),
            () => Now);

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
    }

    [Fact]
    public void Expired_token_requires_authentication_without_calling_the_api()
    {
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => Fixture.Read(Path.Combine("Claude", "auth-expired.json")),
            () => Now);

        var result = store.Load();

        Assert.Null(result.Credentials);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
        Assert.Contains("expired", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_credentials_file_is_a_parse_error()
    {
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => "{ not json",
            () => Now);

        var result = store.Load();

        Assert.Equal(ProviderErrorKind.Parse, result.Error!.Kind);
    }

    [Fact]
    public void Credential_text_never_contains_the_token()
    {
        var store = new ClaudeAuthFileStore(
            () => new Dictionary<string, string>(),
            UserProfile,
            _ => Fixture.Read(Path.Combine("Claude", "auth-healthy.json")),
            () => Now);

        var text = store.Load().Credentials!.ToString();

        Assert.DoesNotContain("test-claude-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test-claude-refresh", text, StringComparison.Ordinal);
    }
}
