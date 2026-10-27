using CodexBar.Core.Providers;

namespace CodexBar.Core.Presentation;

/// <summary>Web destinations for one provider: its own usage console and its service status page.</summary>
public sealed record ProviderLinkTarget(string? DashboardUrl, string? StatusUrl)
{
    /// <summary>Neither link exists, e.g. a provider with no public console.</summary>
    public static ProviderLinkTarget None { get; } = new(null, null);

    public bool HasDashboard => !string.IsNullOrWhiteSpace(this.DashboardUrl);

    public bool HasStatusPage => !string.IsNullOrWhiteSpace(this.StatusUrl);
}

/// <summary>
/// Mirrors the descriptors the macOS build ships, restricted to the providers this port supports.
/// Kept in Core so the flyout menu and any later CLI output agree on the same destinations.
/// </summary>
public static class ProviderLinks
{
    public static ProviderLinkTarget For(ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Value switch
        {
            "codex" => new("https://chatgpt.com/settings/usage?tab=overview", "https://status.openai.com/"),
            "claude" => new("https://claude.ai/settings/usage", "https://status.claude.com/"),
            "copilot" => new("https://github.com/settings/copilot", "https://www.githubstatus.com/"),
            "cursor" => new("https://cursor.com/dashboard?tab=usage", "https://status.cursor.com"),
            "antigravity" => new(
                DashboardUrl: null,
                "https://www.google.com/appsstatus/dashboard/products/npdyhgECDJ6tB66MxXyo/history"),
            "opencode" => new("https://opencode.ai/auth", StatusUrl: null),
            _ => ProviderLinkTarget.None,
        };
    }
}
