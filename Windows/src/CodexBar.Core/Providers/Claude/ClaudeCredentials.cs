namespace CodexBar.Core.Providers.Claude;

/// <summary>OAuth credentials written by the Claude CLI. Read-only.</summary>
public sealed record ClaudeCredentials(
    string AccessToken,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? Scopes = null)
{
    /// <summary>Deliberately excludes token material so a credential can never leak through logs.</summary>
    public override string ToString() =>
        $"ClaudeCredentials(expiresAt: {this.ExpiresAt?.ToString("u") ?? "unknown"}, refreshToken: {(this.RefreshToken is null ? "none" : "present")}, scopes: {this.Scopes?.Count ?? 0})";
}

/// <summary>Outcome of reading the Claude credential file.</summary>
public sealed record ClaudeAuthLoadResult
{
    private ClaudeAuthLoadResult(ClaudeCredentials? credentials, ProviderError? error)
    {
        this.Credentials = credentials;
        this.Error = error;
    }

    public ClaudeCredentials? Credentials { get; }

    public ProviderError? Error { get; }

    public static ClaudeAuthLoadResult Found(ClaudeCredentials credentials) =>
        new(credentials ?? throw new ArgumentNullException(nameof(credentials)), error: null);

    public static ClaudeAuthLoadResult Missing(ProviderError error) =>
        new(credentials: null, error ?? throw new ArgumentNullException(nameof(error)));
}
