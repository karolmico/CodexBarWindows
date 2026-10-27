namespace CodexBar.Core.Providers.OpenCode;

/// <summary>How the OpenCode credential authenticates the usage endpoint.</summary>
public enum OpenCodeCredentialKind
{
    /// <summary>A raw API key, with no expiry of its own.</summary>
    ApiKey,

    /// <summary>The OpenCode CLI's OAuth access token, which carries an expiry.</summary>
    OAuth,
}

/// <summary>
/// A credential for the OpenCode usage endpoint. The token is only ever placed in an Authorization
/// header; it is never part of an error message, a snapshot, or a log line.
/// </summary>
public sealed record OpenCodeCredential
{
    public OpenCodeCredential(string token, OpenCodeCredentialKind kind, DateTimeOffset? expiresAt = null)
    {
        var trimmed = Normalise(token);
        if (trimmed is null)
        {
            throw new ArgumentException("An OpenCode credential needs a token without whitespace.", nameof(token));
        }

        this.Token = trimmed;
        this.Kind = kind;
        this.ExpiresAt = expiresAt;
    }

    public string Token { get; }

    public OpenCodeCredentialKind Kind { get; }

    /// <summary>Only set for <see cref="OpenCodeCredentialKind.OAuth"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    public bool IsExpired(DateTimeOffset now) => this.ExpiresAt is { } expiry && expiry <= now;

    /// <summary>Trims surrounding quotes and rejects empty values or embedded whitespace.</summary>
    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().Trim('"').Trim('\'');
        if (trimmed.Length == 0 || trimmed.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return null;
        }

        return trimmed;
    }

    public override string ToString() => $"OpenCodeCredential({this.Kind})";
}

/// <summary>
/// Outcome of credential discovery: a usable credential, no credential at all, or a specific reason
/// the stored credential cannot be used (for example an expired sign-in).
/// </summary>
public sealed record OpenCodeCredentialLookup(OpenCodeCredential? Credential, ProviderError? Error)
{
    public static OpenCodeCredentialLookup None { get; } = new(null, null);

    public static OpenCodeCredentialLookup Found(OpenCodeCredential credential) =>
        new(credential ?? throw new ArgumentNullException(nameof(credential)), null);

    public static OpenCodeCredentialLookup Failed(ProviderError error) =>
        new(null, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>Platform seam: reads the credential OpenCode's own CLI saved, read-only.</summary>
public interface IOpenCodeCredentialSource
{
    Task<OpenCodeCredentialLookup> ReadAsync(CancellationToken cancellationToken);
}
