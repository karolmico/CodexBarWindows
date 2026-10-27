using System.IO;
using System.Text.Json;

namespace CodexBar.Core.Providers.Cursor;

/// <summary>
/// Locates Cursor's local session without prompting, decrypting browser data, or writing anything.
/// Sources, in order: the Cursor app's VS Code state database, then the <c>cursor-agent</c> CLI
/// auth file. An encrypted app token fails closed; the app never guesses or bypasses OS protection.
/// </summary>
public sealed class CursorSessionStore
{
    private readonly string applicationDataPath;
    private readonly string userProfilePath;
    private readonly Func<string, string?> readAllText;
    private readonly Func<string, string?> readStateToken;
    private readonly Func<DateTimeOffset> clock;

    public CursorSessionStore(
        string? applicationDataPath = null,
        string? userProfilePath = null,
        Func<string, string?>? readAllText = null,
        Func<string, string?>? readStateToken = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.applicationDataPath = string.IsNullOrWhiteSpace(applicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : applicationDataPath;
        this.userProfilePath = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.readStateToken = readStateToken ?? CursorStateDatabase.ReadAccessToken;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string StateDatabasePath() =>
        Path.Combine(this.applicationDataPath, "Cursor", "User", "globalStorage", "state.vscdb");

    /// <summary>Known locations of the Cursor CLI session file, most specific first.</summary>
    public IReadOnlyList<string> AgentAuthPaths() =>
    [
        Path.Combine(this.userProfilePath, ".cursor", "auth.json"),
        Path.Combine(this.applicationDataPath, "cursor", "auth.json"),
    ];

    public CursorAuthLoadResult Load()
    {
        var databasePath = this.StateDatabasePath();
        var databaseExists = File.Exists(databasePath);

        string? stateToken;
        try
        {
            stateToken = this.readStateToken(databasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CursorAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.Unknown,
                "Could not read Cursor's local session. Restart Cursor and try again.",
                cause: exception));
        }

        if (stateToken is not null)
        {
            var resolved = this.Resolve(stateToken);
            if (resolved.Session is not null)
            {
                return resolved;
            }

            if (resolved.Error?.Kind is ProviderErrorKind.Parse or ProviderErrorKind.Unknown
                || (resolved.Error?.Kind == ProviderErrorKind.AuthenticationRequired
                    && !stateToken.StartsWith("cursor-safe-storage:", StringComparison.Ordinal)))
            {
                // An expired or malformed app token is authoritative: do not silently swap in a
                // different CLI account.
                return resolved;
            }

            var fallback = this.LoadAgentAuth();
            return fallback.Session is not null || fallback.Error?.Kind != ProviderErrorKind.NotConfigured
                ? fallback
                : resolved;
        }

        var agent = this.LoadAgentAuth();
        if (agent.Session is not null || agent.Error?.Kind != ProviderErrorKind.NotConfigured)
        {
            return agent;
        }

        return CursorAuthLoadResult.Missing(ProviderError.NotConfigured(
            databaseExists
                ? "Cursor is installed but no account is signed in. Sign in to Cursor, then refresh."
                : "Cursor was not found on this PC. Install Cursor or the Cursor CLI, then refresh."));
    }

    private CursorAuthLoadResult LoadAgentAuth()
    {
        foreach (var path in this.AgentAuthPaths())
        {
            string? text;
            try
            {
                text = this.readAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return CursorAuthLoadResult.Missing(new ProviderError(
                    ProviderErrorKind.Unknown,
                    "Could not read the Cursor CLI session. Run `cursor-agent login` and try again.",
                    cause: exception));
            }

            if (text is null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("accessToken", out var token)
                    && token.ValueKind == JsonValueKind.String
                    && token.GetString() is { } value
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return this.Resolve(value.Trim());
                }

                return CursorAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "The Cursor CLI session has no access token. Run `cursor-agent login`."));
            }
            catch (JsonException exception)
            {
                return CursorAuthLoadResult.Missing(new ProviderError(
                    ProviderErrorKind.Parse,
                    "Could not decode the Cursor CLI session. Run `cursor-agent login` again.",
                    cause: exception));
            }
        }

        return CursorAuthLoadResult.Missing(ProviderError.NotConfigured("No Cursor CLI session found."));
    }

    private CursorAuthLoadResult Resolve(string token)
    {
        if (token.StartsWith("cursor-safe-storage:", StringComparison.Ordinal))
        {
            return CursorAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.AuthenticationRequired,
                "Cursor stores this session encrypted. Run `cursor-agent login` so CodexBar can read "
                + "a plain token, then refresh."));
        }

        if (!CursorToken.TryDecode(token, out var userId, out var email, out var expiresAt))
        {
            return CursorAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.Parse,
                "The Cursor access token could not be decoded. Sign in to Cursor again."));
        }

        if (expiresAt <= this.clock().AddMinutes(1))
        {
            return CursorAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                "The Cursor session has expired. Sign in to the Cursor app again."));
        }

        return CursorAuthLoadResult.Found(new CursorSession(token, userId, email, expiresAt));
    }

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;
}
