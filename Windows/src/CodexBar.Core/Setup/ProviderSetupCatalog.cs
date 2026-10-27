using CodexBar.Core.Providers;

namespace CodexBar.Core.Setup;

/// <summary>
/// Static instructions for connecting one provider. It is deliberately plain copy: the catalog never
/// holds a token, and it names only the sign-in command and the file CodexBar reads so the user can
/// reproduce the result without guessing.
/// </summary>
public sealed record ProviderSetup
{
    public ProviderSetup(
        ProviderKey key,
        string displayName,
        string summary,
        IReadOnlyList<string> steps,
        string? command = null,
        string? credentialsPath = null,
        string? pathNote = null,
        string? helpUrl = null,
        string? helpLabel = null,
        bool supportsPersonalAccessToken = false,
        string? tokenLabel = null,
        string? tokenHint = null)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
        this.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("A provider setup needs a display name.", nameof(displayName))
            : displayName.Trim();
        this.Summary = summary?.Trim() ?? string.Empty;
        this.Steps = steps ?? [];
        this.Command = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        this.CredentialsPath = string.IsNullOrWhiteSpace(credentialsPath) ? null : credentialsPath.Trim();
        this.PathNote = string.IsNullOrWhiteSpace(pathNote) ? null : pathNote.Trim();
        this.HelpUrl = string.IsNullOrWhiteSpace(helpUrl) ? null : helpUrl.Trim();
        this.HelpLabel = string.IsNullOrWhiteSpace(helpLabel) ? null : helpLabel.Trim();
        this.SupportsPersonalAccessToken = supportsPersonalAccessToken;
        this.TokenLabel = string.IsNullOrWhiteSpace(tokenLabel) ? null : tokenLabel.Trim();
        this.TokenHint = string.IsNullOrWhiteSpace(tokenHint) ? null : tokenHint.Trim();
    }

    public ProviderKey Key { get; }

    public string DisplayName { get; }

    /// <summary>One sentence about where the credential comes from.</summary>
    public string Summary { get; }

    /// <summary>Ordered instructions, each one short line.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The exact command to run, when the provider signs in through a CLI.</summary>
    public string? Command { get; }

    /// <summary>
    /// Where CodexBar reads the credential. Environment tokens such as <c>%USERPROFILE%</c> are kept
    /// literal for display; call <see cref="ExpandedCredentialsPath"/> before touching the file system.
    /// </summary>
    public string? CredentialsPath { get; }

    /// <summary>Extra note about an environment override for <see cref="CredentialsPath"/>.</summary>
    public string? PathNote { get; }

    public string? HelpUrl { get; }

    public string? HelpLabel { get; }

    /// <summary>True when this app can call the provider with a token the user pastes here.</summary>
    public bool SupportsPersonalAccessToken { get; }

    public string? TokenLabel { get; }

    public string? TokenHint { get; }

    public string? ExpandedCredentialsPath =>
        this.CredentialsPath is null ? null : Environment.ExpandEnvironmentVariables(this.CredentialsPath);
}

/// <summary>
/// The single source of truth for "how do I connect this provider" shown by the setup window and
/// printed by the CLI. Copy stays here so the UI and tests agree on every command and path.
/// </summary>
public static class ProviderSetupCatalog
{
    private static readonly ProviderSetup[] Entries =
    [
        new(
            ProviderKeys.Codex,
            "Codex",
            "CodexBar reads the sign-in the OpenAI Codex CLI already saved. It never asks for your OpenAI password.",
            [
                "Install the Codex CLI if you have not: npm install -g @openai/codex",
                "Run the sign-in command in a terminal and finish the browser step.",
                "Return here and choose Check connection.",
            ],
            command: "codex login",
            credentialsPath: @"%USERPROFILE%\.codex\auth.json",
            pathNote: "With CODEX_HOME set, the CLI keeps its profile in %CODEX_HOME%\\auth.json instead.",
            helpUrl: "https://github.com/openai/codex",
            helpLabel: "Codex CLI on GitHub"),
        new(
            ProviderKeys.Claude,
            "Claude",
            "CodexBar reads the sign-in Claude Code already saved. It never asks for your Anthropic password.",
            [
                "Install Claude Code if you have not: npm install -g @anthropic-ai/claude-code",
                "Run `claude`, then use /login to finish the browser sign-in.",
                "Return here and choose Check connection.",
            ],
            command: "claude",
            credentialsPath: @"%USERPROFILE%\.claude\.credentials.json",
            pathNote: "With CLAUDE_CONFIG_DIR set, the CLI keeps .credentials.json in that folder instead.",
            helpUrl: "https://github.com/anthropics/claude-code",
            helpLabel: "Claude Code on GitHub"),
        new(
            ProviderKeys.Gemini,
            "Gemini",
            "CodexBar reads the Google sign-in saved by the Gemini CLI.",
            [
                "Install the Gemini CLI if you have not: npm install -g @google/gemini-cli",
                "Run `gemini` and choose Login with Google.",
                "Return here and choose Check connection.",
            ],
            command: "gemini",
            credentialsPath: @"%USERPROFILE%\.gemini\oauth_creds.json",
            helpUrl: "https://github.com/google-gemini/gemini-cli",
            helpLabel: "Gemini CLI on GitHub"),
        new(
            ProviderKeys.Copilot,
            "Copilot",
            "GitHub Copilot needs a GitHub token. Sign in with the GitHub CLI, or save a personal access token below.",
            [
                "Sign in with the GitHub CLI: `gh auth login` and choose GitHub.com.",
                "Or create a classic personal access token with the copilot scope and save it below.",
                "Return here and choose Check connection.",
            ],
            command: "gh auth login",
            helpUrl: "https://github.com/settings/tokens",
            helpLabel: "Create a token",
            supportsPersonalAccessToken: true,
            tokenLabel: "GitHub personal access token",
            tokenHint: "Stored encrypted for this Windows account. Leave it empty to use the GitHub CLI or GITHUB_TOKEN."),
        new(
            ProviderKeys.Cursor,
            "Cursor",
            "CodexBar reads the session of the Cursor app on this PC. It never sees your Cursor password.",
            [
                "Open the Cursor app and sign in.",
                "Keep Cursor signed in; CodexBar only reads its local session.",
                "Return here and choose Check connection.",
            ],
            credentialsPath: @"%APPDATA%\Cursor\User\globalStorage\state.vscdb",
            helpUrl: "https://cursor.com",
            helpLabel: "Open Cursor"),
        new(
            ProviderKeys.Antigravity,
            "Antigravity",
            "CodexBar asks the Antigravity language server running on this PC for quota. No token is stored.",
            [
                "Open Antigravity and sign in.",
                "Keep it running; CodexBar reads usage from its local language server.",
                "Return here and choose Check connection.",
            ],
            helpUrl: "https://antigravity.google",
            helpLabel: "Open Antigravity"),
    ];

    /// <summary>Every setup entry, in catalog order.</summary>
    public static IReadOnlyList<ProviderSetup> All => Entries;

    public static ProviderSetup? Find(ProviderKey key) =>
        Entries.FirstOrDefault(setup => setup.Key == key);

    /// <summary>
    /// Entries for the given providers in their configured order; providers without an entry are
    /// skipped, so a new provider cannot break the setup window before its copy lands.
    /// </summary>
    public static IReadOnlyList<ProviderSetup> For(IEnumerable<ProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        return providers
            .Select(descriptor => Find(descriptor.Key))
            .OfType<ProviderSetup>()
            .ToArray();
    }
}
