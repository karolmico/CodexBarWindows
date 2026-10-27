using System.IO;
using System.Text.Json;

namespace CodexBar.Core.Providers.Gemini;

/// <summary>
/// Reads <c>~/.gemini/oauth_creds.json</c>. An expired access token is reported as an authentication
/// problem: refreshing requires the Gemini CLI's OAuth client credentials, which this app does not own.
/// </summary>
public sealed class GeminiAuthFileStore
{
    private readonly string userProfilePath;
    private readonly Func<string, string?> readAllText;
    private readonly Func<DateTimeOffset> clock;

    public GeminiAuthFileStore(
        string? userProfilePath = null,
        Func<string, string?>? readAllText = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.userProfilePath = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string CredentialsPath() =>
        Path.Combine(this.userProfilePath, ".gemini", "oauth_creds.json");

    public GeminiAuthLoadResult Load()
    {
        string text;
        try
        {
            var content = this.readAllText(this.CredentialsPath());
            if (content is null)
            {
                return GeminiAuthLoadResult.Missing(ProviderError.NotConfigured(
                    "Gemini credentials not found. Run `gemini` to sign in."));
            }

            text = content;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return GeminiAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.AuthenticationRequired,
                "Gemini credentials could not be read. Check their permissions or run `gemini` again.",
                cause: exception));
        }

        return Parse(text, this.clock());
    }

    private static GeminiAuthLoadResult Parse(string text, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out var token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(token.GetString()))
            {
                return GeminiAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "Gemini credentials contain no access token. Run `gemini` to sign in."));
            }

            var expiresAt = root.TryGetProperty("expiry_date", out var expiry) && expiry.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)expiry.GetDouble())
                : (DateTimeOffset?)null;

            if (expiresAt is { } value && value <= now)
            {
                return GeminiAuthLoadResult.Missing(ProviderError.AuthenticationRequired(
                    "Gemini credentials expired. Run `gemini` to sign in again."));
            }

            return GeminiAuthLoadResult.Found(new GeminiCredentials(
                token.GetString()!.Trim(),
                String(root, "refresh_token"),
                String(root, "id_token"),
                expiresAt));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return GeminiAuthLoadResult.Missing(new ProviderError(
                ProviderErrorKind.Parse,
                "Failed to decode Gemini credentials. Run `gemini` to sign in again.",
                cause: exception));
        }
    }

    private static string? String(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;
}
