using CodexBar.Core.Providers;
using CodexBar.Core.Setup;
using Xunit;

namespace CodexBar.Core.Tests.Setup;

public sealed class ProviderSetupCatalogTests
{
    [Fact]
    public void Every_shipped_provider_has_exactly_one_setup_entry()
    {
        ProviderKey[] keys =
        [
            ProviderKeys.Codex,
            ProviderKeys.Claude,
            ProviderKeys.Gemini,
            ProviderKeys.Copilot,
            ProviderKeys.Cursor,
            ProviderKeys.Antigravity,
        ];

        Assert.Equal(keys, ProviderSetupCatalog.All.Select(setup => setup.Key));
    }

    [Fact]
    public void Every_entry_explains_where_the_credential_comes_from_and_what_to_do()
    {
        Assert.All(ProviderSetupCatalog.All, setup =>
        {
            Assert.False(string.IsNullOrWhiteSpace(setup.Summary));
            Assert.True(setup.Steps.Count >= 2, $"{setup.Key} needs at least two steps.");
            Assert.All(setup.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
            Assert.True(
                setup.Command is not null || setup.CredentialsPath is not null || setup.HelpUrl is not null,
                $"{setup.Key} needs a command, a credential file, or a help page.");
        });
    }

    [Fact]
    public void Codex_entry_names_the_cli_command_and_auth_file()
    {
        var setup = Assert.IsType<ProviderSetup>(ProviderSetupCatalog.Find(ProviderKeys.Codex));

        Assert.Equal("codex login", setup.Command);
        Assert.Equal(@"%USERPROFILE%\.codex\auth.json", setup.CredentialsPath);
        Assert.Contains("CODEX_HOME", setup.PathNote, StringComparison.Ordinal);
        Assert.False(setup.SupportsPersonalAccessToken);
    }

    [Fact]
    public void Claude_and_gemini_entries_use_their_own_cli_commands()
    {
        var claude = Assert.IsType<ProviderSetup>(ProviderSetupCatalog.Find(ProviderKeys.Claude));
        var gemini = Assert.IsType<ProviderSetup>(ProviderSetupCatalog.Find(ProviderKeys.Gemini));

        Assert.Equal("claude", claude.Command);
        Assert.Equal(@"%USERPROFILE%\.claude\.credentials.json", claude.CredentialsPath);
        Assert.Contains("CLAUDE_CONFIG_DIR", claude.PathNote, StringComparison.Ordinal);

        Assert.Equal("gemini", gemini.Command);
        Assert.Equal(@"%USERPROFILE%\.gemini\oauth_creds.json", gemini.CredentialsPath);
    }

    [Fact]
    public void Copilot_is_the_only_provider_with_a_token_override()
    {
        var withTokens = ProviderSetupCatalog.All
            .Where(setup => setup.SupportsPersonalAccessToken)
            .ToArray();

        var copilot = Assert.Single(withTokens);
        Assert.Equal(ProviderKeys.Copilot, copilot.Key);
        Assert.Equal("gh auth login", copilot.Command);
        Assert.Equal("https://github.com/settings/tokens", copilot.HelpUrl);
        Assert.False(string.IsNullOrWhiteSpace(copilot.TokenLabel));
        Assert.False(string.IsNullOrWhiteSpace(copilot.TokenHint));
    }

    [Fact]
    public void For_keeps_the_descriptor_order_and_skips_providers_without_copy()
    {
        ProviderDescriptor[] descriptors =
        [
            new(ProviderKeys.Copilot, "Copilot"),
            new(ProviderKey.Parse("future-provider"), "Future"),
            new(ProviderKeys.Claude, "Claude"),
        ];

        var setups = ProviderSetupCatalog.For(descriptors);

        Assert.Equal([ProviderKeys.Copilot, ProviderKeys.Claude], setups.Select(setup => setup.Key));
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_provider()
    {
        Assert.Null(ProviderSetupCatalog.Find(ProviderKey.Parse("future-provider")));
    }
}
