using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Credentials;

/// <summary>
/// Encrypted JSON file holding per-provider token overrides. Every read fails closed: a missing,
/// corrupt, or undecryptable file behaves exactly like "no token saved", so a provider reports
/// <c>not configured</c> instead of the app guessing or writing a partial secret.
/// </summary>
public sealed class FileProviderTokenStore : IProviderTokenStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly object gate = new();
    private readonly string filePath;
    private readonly IDataProtector protector;
    private readonly Func<string, string?> readAllText;
    private readonly Action<string, string> writeAllText;

    public FileProviderTokenStore(
        string filePath,
        IDataProtector protector,
        Func<string, string?>? readAllText = null,
        Action<string, string>? writeAllText = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A token store needs a file path.", nameof(filePath));
        }

        this.filePath = filePath;
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.writeAllText = writeAllText ?? WriteAtomically;
    }

    /// <summary>Where the encrypted overrides live. Safe to show in the UI; it contains no secret itself.</summary>
    public string FilePath => this.filePath;

    public bool HasToken(ProviderKey provider) => this.GetToken(provider) is not null;

    public string? GetToken(ProviderKey provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (this.gate)
        {
            if (!this.LoadProtectedTokens().TryGetValue(provider.Value, out var ciphertext))
            {
                return null;
            }

            try
            {
                return Decode(this.protector.Unprotect(ciphertext));
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                // A token saved under another Windows account or with a rotated DPAPI key cannot be
                // recovered by this process. Treat it as absent rather than failing every refresh.
                return null;
            }
        }
    }

    public void SaveToken(ProviderKey provider, string token)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A saved token cannot be empty.", nameof(token));
        }

        var trimmed = token.Trim();
        byte[] ciphertext;
        try
        {
            ciphertext = this.protector.Protect(Utf8.GetBytes(trimmed));
        }
        catch (CryptographicException exception)
        {
            throw new CredentialStoreException(
                "The token could not be encrypted for this Windows account.",
                exception);
        }

        lock (this.gate)
        {
            var tokens = this.LoadProtectedTokens();
            tokens[provider.Value] = ciphertext;
            this.Write(tokens);
        }
    }

    public void ClearToken(ProviderKey provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (this.gate)
        {
            var tokens = this.LoadProtectedTokens();
            if (!tokens.Remove(provider.Value))
            {
                return;
            }

            this.Write(tokens);
        }
    }

    private void Write(Dictionary<string, byte[]> tokens)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in tokens)
        {
            payload[key] = Convert.ToBase64String(value);
        }

        var document = new TokenDocument(CurrentVersion, payload);
        try
        {
            this.writeAllText(this.filePath, JsonSerializer.Serialize(document, WriteOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreException(
                "The token file could not be written. Check permissions on the CodexBar data folder.",
                exception);
        }
    }

    private Dictionary<string, byte[]> LoadProtectedTokens()
    {
        string? text;
        try
        {
            text = this.readAllText(this.filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<TokenDocument>(text, ReadOptions);
            if (document?.Tokens is null || document.Version != CurrentVersion)
            {
                return [];
            }

            var tokens = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (key, value) in document.Tokens)
            {
                if (ProviderKey.TryParse(key, out _) && !string.IsNullOrWhiteSpace(value))
                {
                    tokens[key] = Convert.FromBase64String(value);
                }
            }

            return tokens;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return [];
        }
    }

    private static string Decode(byte[] plaintext) => Utf8.GetString(plaintext).Trim();

    private static string? ReadFileOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>Writes beside the target and swaps, so a crash never leaves a truncated token file.</summary>
    private static void WriteAtomically(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text, Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record TokenDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("tokens")] Dictionary<string, string> Tokens);
}

/// <summary>A credential write failed; the message is safe to show and never contains the token.</summary>
public sealed class CredentialStoreException : Exception
{
    public CredentialStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
