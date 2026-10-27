namespace CodexBar.Core.Providers.Codex;

/// <summary>
/// Credentials read from the Codex CLI's own <c>auth.json</c>. Read-only: the app never writes this file.
/// </summary>
public sealed record CodexCredentials(
    string AccessToken,
    string? AccountId = null,
    string? RefreshToken = null,
    string? IdToken = null,
    DateTimeOffset? LastRefresh = null,
    bool IsApiKey = false)
{
    /// <summary>Deliberately excludes token material so a credential can never leak through logs.</summary>
    public override string ToString() =>
        $"CodexCredentials(accountId: {this.AccountId ?? "none"}, apiKey: {this.IsApiKey}, refreshToken: {(this.RefreshToken is null ? "none" : "present")})";
}

/// <summary>Outcome of reading <c>auth.json</c>: either credentials or an actionable provider error.</summary>
public sealed record CodexAuthLoadResult
{
    private CodexAuthLoadResult(CodexCredentials? credentials, ProviderError? error)
    {
        this.Credentials = credentials;
        this.Error = error;
    }

    public CodexCredentials? Credentials { get; }

    public ProviderError? Error { get; }

    public static CodexAuthLoadResult Found(CodexCredentials credentials) =>
        new(credentials ?? throw new ArgumentNullException(nameof(credentials)), error: null);

    public static CodexAuthLoadResult Missing(ProviderError error) =>
        new(credentials: null, error ?? throw new ArgumentNullException(nameof(error)));
}
