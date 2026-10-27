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
            ProviderKeys.Copilot,
            ProviderKeys.Cursor,
            ProviderKeys.Antigravity,
            ProviderKeys.OpenCode,
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
    public void Claude_entry_uses_its_own_cli_command()
    {
        var claude = Assert.IsType<ProviderSetup>(ProviderSetupCatalog.Find(ProviderKeys.Claude));

        Assert.Equal("claude", claude.Command);
        Assert.Equal(@"%USERPROFILE%\.claude\.credentials.json", claude.CredentialsPath);
        Assert.Contains("CLAUDE_CONFIG_DIR", claude.PathNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Copilot_and_OpenCode_offer_a_token_override()
    {
        var withTokens = ProviderSetupCatalog.All
            .Where(setup => setup.SupportsPersonalAccessToken)
            .ToArray();

        Assert.Equal([ProviderKeys.Copilot, ProviderKeys.OpenCode], withTokens.Select(setup => setup.Key));

        var copilot = withTokens[0];
        Assert.Equal("gh auth login", copilot.Command);
        Assert.Equal("https://github.com/settings/tokens", copilot.HelpUrl);
        Assert.False(string.IsNullOrWhiteSpace(copilot.TokenLabel));
        Assert.False(string.IsNullOrWhiteSpace(copilot.TokenHint));

        var openCode = withTokens[1];
        Assert.Equal("opencode auth login", openCode.Command);
        Assert.Equal("https://opencode.ai/auth", openCode.HelpUrl);
        Assert.False(string.IsNullOrWhiteSpace(openCode.TokenLabel));
        Assert.False(string.IsNullOrWhiteSpace(openCode.TokenHint));
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
