using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CodexBar.Core.Providers.Claude;

/// <summary>
/// Locates and parses <c>.credentials.json</c> from the Claude CLI configuration. <c>CLAUDE_CONFIG_DIR</c>
/// wins; otherwise the per-user <c>.claude</c> directory is used. An expired token is reported as an
/// authentication problem rather than silently retried, because this app never writes credentials.
/// </summary>
public sealed class ClaudeAuthFileStore
{
    private readonly Func<IReadOnlyDictionary<string, string>> environment;
    private readonly string userProfilePath;
    private readonly Func<string, string?> readAllText;
    private readonly Func<DateTimeOffset> clock;

    public ClaudeAuthFileStore(
        Func<IReadOnlyDictionary<string, string>>? environment = null,
        string? userProfilePath = null,
        Func<string, string?>? readAllText = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.environment = environment ?? ReadEnvironment;
        this.userProfilePath = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string CredentialsPath()
    {
        var environment = this.environment();
        var configDirectory = environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configured)
            ? configured?.Trim()
            : null;

        var root = string.IsNullOrEmpty(configDirectory)
            ? Path.Combine(this.userProfilePath, ".claude")
            : configDirectory;

        return Path.Combine(root, ".credentials.json");
    }

    public ClaudeAuthLoadResult Load()
    {
        string text;
        try
        {
            var content = this.readAllText(this.CredentialsPath());
            if (content is null)
            {
                return ClaudeAuthLoadResult.Missing(ProviderError.NotConfigured(
                    "Claude credentials not found. Run `claude` to sign in."));
            }

            text = content;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ClaudeAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.AuthenticationRequired,
                "Claude credentials could not be read. Check their permissions or run `claude` again.",
                cause: exception));
        }

        return Parse(text, this.clock());
    }

    private static ClaudeAuthLoadResult Parse(string text, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object)
            {
                return ClaudeAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "Claude credentials contain no OAuth session. Run `claude` to sign in."));
            }

            var accessToken = GetString(oauth, "accessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ClaudeAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "Claude credentials contain no access token. Run `claude` to sign in."));
            }

            var expiresAt = GetNumber(oauth, "expiresAt") is { } millis
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)millis)
                : (DateTimeOffset?)null;

            if (expiresAt is { } expiry && expiry <= now)
            {
                return ClaudeAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "Claude credentials expired. Run `claude` to sign in again."));
            }

            var scopes = GetStringArray(oauth, "scopes");

            return ClaudeAuthLoadResult.Found(new ClaudeCredentials(
                accessToken.Trim(),
                GetString(oauth, "refreshToken")?.Trim(),
                expiresAt,
                scopes));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return ClaudeAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.Parse,
                "Failed to decode Claude credentials. Run `claude` to sign in again.",
                cause: exception));
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static IReadOnlyList<string>? GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .Where(static entry => entry.ValueKind == JsonValueKind.String)
            .Select(static entry => entry.GetString()!)
            .ToArray();
    }

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            values["CLAUDE_CONFIG_DIR"] = configDirectory;
        }

        return values;
    }
}
