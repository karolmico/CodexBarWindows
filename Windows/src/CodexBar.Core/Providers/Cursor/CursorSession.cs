using System.Globalization;
using System.Text.Json;

namespace CodexBar.Core.Providers.Cursor;

/// <summary>
/// One Cursor session recovered from local app state. The access token is a JWT whose payload names
/// the account and expiry; the token is never logged or written anywhere.
/// </summary>
public sealed record CursorSession(string AccessToken, string UserId, string? Email, DateTimeOffset ExpiresAt)
{
    /// <summary>Cookie value Cursor's web API expects: the user id, then the raw JWT.</summary>
    public string CookieValue =>
        string.Create(CultureInfo.InvariantCulture, $"{this.UserId}%3A%3A{this.AccessToken}");

    public override string ToString() =>
        $"CursorSession(userId: {this.UserId}, expiresAt: {this.ExpiresAt.ToString("u", CultureInfo.InvariantCulture)})";
}

public sealed record CursorAuthLoadResult
{
    private CursorAuthLoadResult(CursorSession? session, ProviderError? error)
    {
        this.Session = session;
        this.Error = error;
    }

    public CursorSession? Session { get; }

    public ProviderError? Error { get; }

    public static CursorAuthLoadResult Found(CursorSession session) =>
        new(session ?? throw new ArgumentNullException(nameof(session)), error: null);

    public static CursorAuthLoadResult Missing(ProviderError error) =>
        new(session: null, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>
/// Decodes the Cursor access token without validating its signature. Validation is the API's job;
/// this only needs the account id and expiry to decide whether the stored session is usable.
/// </summary>
public static class CursorToken
{
    public static bool TryDecode(
        string accessToken,
        out string userId,
        out string? email,
        out DateTimeOffset expiresAt)
    {
        userId = string.Empty;
        email = null;
        expiresAt = default;

        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var rawSubject = subject.GetString()?.Trim();
            if (string.IsNullOrEmpty(rawSubject))
            {
                return false;
            }

            // Auth0 subjects look like "auth0|user_01ABC"; the API wants the part after the separator.
            var resolvedUserId = rawSubject.Split('|', StringSplitOptions.RemoveEmptyEntries).Last();
            if (resolvedUserId.Length == 0
                || !resolvedUserId.All(static character =>
                    char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            {
                return false;
            }

            if (!root.TryGetProperty("exp", out var expiration) || expiration.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            userId = resolvedUserId;
            email = root.TryGetProperty("email", out var emailValue) && emailValue.ValueKind == JsonValueKind.String
                ? emailValue.GetString()?.Trim()
                : null;
            expiresAt = DateTimeOffset.FromUnixTimeSeconds((long)expiration.GetDouble());
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
