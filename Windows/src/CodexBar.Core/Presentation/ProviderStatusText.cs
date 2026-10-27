using CodexBar.Core.Providers;

namespace CodexBar.Core.Presentation;

/// <summary>Short, human copy for a provider error; shared by the tray and the flyout.</summary>
public static class ProviderStatusText
{
    public static string Label(ProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind switch
        {
            ProviderErrorKind.NotConfigured => "not configured",
            ProviderErrorKind.AuthenticationRequired => "sign-in required",
            ProviderErrorKind.RateLimited => "rate limited",
            ProviderErrorKind.Network => "offline",
            ProviderErrorKind.Parse => "unreadable response",
            _ => "error",
        };
    }
}
