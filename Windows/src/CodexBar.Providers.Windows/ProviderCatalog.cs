using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Antigravity;
using CodexBar.Core.Providers.Claude;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Providers.Copilot;
using CodexBar.Core.Providers.Cursor;
using CodexBar.Core.Providers.Gemini;

namespace CodexBar.Providers.Windows;

/// <summary>
/// The one place that wires concrete providers for a Windows process. The tray app and the CLI share
/// it so both always expose the same catalog and never drift.
/// </summary>
public static class ProviderCatalog
{
    public static ProviderRegistry Create(HttpClient httpClient, IProviderTokenStore? tokenStore = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        return new ProviderRegistry(
        [
            new CodexProvider(new CodexAuthFileStore(), new CodexUsageClient(httpClient)),
            new ClaudeProvider(new ClaudeAuthFileStore(), new ClaudeUsageClient(httpClient)),
            new GeminiProvider(new GeminiAuthFileStore(), new GeminiUsageClient(httpClient)),
            new CopilotProvider(
                new CopilotTokenSource(tokenStore: tokenStore),
                new CopilotUsageClient(httpClient)),
            new CursorProvider(new CursorSessionStore(), new CursorUsageClient(httpClient)),
            new AntigravityProvider(
                new WindowsAntigravityProcessSource(),
                new AntigravityUsageClient(httpClient)),
        ]);
    }
}
