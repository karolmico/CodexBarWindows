namespace CodexBar.Core.Providers.Gemini;

/// <summary>OAuth credentials written by the Gemini CLI. Read-only.</summary>
public sealed record GeminiCredentials(
    string AccessToken,
    string? RefreshToken = null,
    string? IdToken = null,
    DateTimeOffset? ExpiresAt = null)
{
    public override string ToString() =>
        $"GeminiCredentials(expiresAt: {this.ExpiresAt?.ToString("u") ?? "unknown"}, refreshToken: {(this.RefreshToken is null ? "none" : "present")})";
}

public sealed record GeminiAuthLoadResult
{
    private GeminiAuthLoadResult(GeminiCredentials? credentials, ProviderError? error)
    {
        this.Credentials = credentials;
        this.Error = error;
    }

    public GeminiCredentials? Credentials { get; }

    public ProviderError? Error { get; }

    public static GeminiAuthLoadResult Found(GeminiCredentials credentials) =>
        new(credentials ?? throw new ArgumentNullException(nameof(credentials)), error: null);

    public static GeminiAuthLoadResult Missing(ProviderError error) =>
        new(credentials: null, error ?? throw new ArgumentNullException(nameof(error)));
}
