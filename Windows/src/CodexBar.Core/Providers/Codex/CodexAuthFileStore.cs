using System.IO;
using System.Text.Json;

namespace CodexBar.Core.Providers.Codex;

/// <summary>
/// Locates and parses the Codex CLI credential file. Resolution mirrors upstream: <c>CODEX_HOME</c> wins,
/// otherwise the per-user <c>.codex</c> directory is used. The environment and file access are injected so
/// the resolution rules are testable and so a changed <c>CODEX_HOME</c> is honoured on the next refresh.
/// </summary>
public sealed class CodexAuthFileStore
{
    private readonly Func<IReadOnlyDictionary<string, string>> environment;
    private readonly string userProfilePath;
    private readonly Func<string, string?> readAllText;

    public CodexAuthFileStore(
        Func<IReadOnlyDictionary<string, string>>? environment = null,
        string? userProfilePath = null,
        Func<string, string?>? readAllText = null)
    {
        this.environment = environment ?? ReadEnvironment;
        this.userProfilePath = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        this.readAllText = readAllText ?? ReadFileOrNull;
    }

    public CodexAuthLoadResult Load()
    {
        string text;
        try
        {
            var content = this.readAllText(this.AuthFilePath());
            if (content is null)
            {
                return CodexAuthLoadResult.Missing(ProviderError.NotConfigured(
                    "Codex auth.json not found. Run `codex login` to sign in."));
            }

            text = content;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CodexAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.AuthenticationRequired,
                "Codex auth.json could not be read. Check its permissions or run `codex login` again.",
                cause: exception));
        }

        return Parse(text);
    }

    /// <summary>Path the next <see cref="Load"/> will read. Exposed for diagnostics and tests.</summary>
    public string AuthFilePath()
    {
        var environment = this.environment();
        var codexHome = environment.TryGetValue("CODEX_HOME", out var configured)
            ? configured?.Trim()
            : null;

        var home = string.IsNullOrEmpty(codexHome)
            ? Path.Combine(this.userProfilePath, ".codex")
            : codexHome;

        return Path.Combine(home, "auth.json");
    }

    private static CodexAuthLoadResult Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseFailure();
            }

            // API-key mode: the CLI stores the key at the root instead of OAuth tokens.
            if (TryGetString(root, "OPENAI_API_KEY") is { Length: > 0 } apiKey)
            {
                return CodexAuthLoadResult.Found(new CodexCredentials(apiKey, IsApiKey: true));
            }

            if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            {
                return MissingTokens();
            }

            var accessToken = TryGetString(tokens, "access_token") ?? TryGetString(tokens, "accessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return MissingTokens();
            }

            var accountId = TryGetString(tokens, "account_id") ?? TryGetString(tokens, "accountId");
            var refreshToken = TryGetString(tokens, "refresh_token") ?? TryGetString(tokens, "refreshToken");
            var idToken = TryGetString(tokens, "id_token") ?? TryGetString(tokens, "idToken");
            var lastRefresh = ParseTimestamp(TryGetString(root, "last_refresh"));

            return CodexAuthLoadResult.Found(new CodexCredentials(
                accessToken.Trim(),
                Normalize(accountId),
                Normalize(refreshToken),
                Normalize(idToken),
                lastRefresh));
        }
        catch (JsonException exception)
        {
            return CodexAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.Parse,
                "Failed to decode Codex credentials. Run `codex login` to sign in again.",
                cause: exception));
        }
    }

    private static CodexAuthLoadResult MissingTokens() =>
        CodexAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
            "Codex auth.json contains no tokens. Run `codex login` to sign in."));

    private static CodexAuthLoadResult ParseFailure() =>
        CodexAuthLoadResult.Missing(new ProviderError(
            ProviderErrorKind.Parse,
            "Failed to decode Codex credentials. Run `codex login` to sign in again."));

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, default, out var parsed)
            ? parsed
            : null;

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            values["CODEX_HOME"] = codexHome;
        }

        return values;
    }
}
