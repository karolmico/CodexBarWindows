using System.IO;
using System.Text.Json;
using CodexBar.Core.Providers;
using Microsoft.Data.Sqlite;

namespace CodexBar.Core.Providers.OpenCode;

/// <summary>
/// Reads the credential the OpenCode CLI already saved, without ever writing to it. Newer CLIs keep
/// one JSON credential per integration in <c>opencode.db</c>; older installs keep an
/// <c>auth.json</c> entry. Both files are opened read-only and a locked or half-written store
/// behaves like "no credential" so a refresh can never take the tray down.
/// </summary>
public sealed class OpenCodeCliCredentialStore : IOpenCodeCredentialSource
{
    /// <summary>The two integrations whose credential can authorise Go usage.</summary>
    private static readonly string[] Integrations = ["opencode-go", "opencode"];

    private readonly string databasePath;
    private readonly string authFilePath;
    private readonly Func<DateTimeOffset> clock;

    public OpenCodeCliCredentialStore(
        string databasePath,
        string authFilePath,
        Func<DateTimeOffset>? clock = null)
    {
        this.databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? throw new ArgumentException("A database path is required.", nameof(databasePath))
            : databasePath;
        this.authFilePath = string.IsNullOrWhiteSpace(authFilePath)
            ? throw new ArgumentException("An auth file path is required.", nameof(authFilePath))
            : authFilePath;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The store for this Windows user, honouring <c>XDG_DATA_HOME</c> and <c>OPENCODE_DB</c>.</summary>
    public static OpenCodeCliCredentialStore ForCurrentUser(Func<DateTimeOffset>? clock = null) =>
        new(DefaultDatabasePath(), DefaultAuthFilePath(), clock);

    public static string DefaultDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome)
            ? dataHome
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        return Path.Combine(root, "opencode");
    }

    public static string DefaultDatabasePath()
    {
        var directory = DefaultDirectory();
        var configured = Environment.GetEnvironmentVariable("OPENCODE_DB");
        if (string.IsNullOrWhiteSpace(configured) || configured == ":memory:")
        {
            return Path.Combine(directory, "opencode.db");
        }

        return Path.IsPathRooted(configured) ? configured : Path.Combine(directory, configured);
    }

    public static string DefaultAuthFilePath() => Path.Combine(DefaultDirectory(), "auth.json");

    public Task<OpenCodeCredentialLookup> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => this.Read(), cancellationToken);

    private OpenCodeCredentialLookup Read()
    {
        if (File.Exists(this.databasePath))
        {
            var fromDatabase = this.ReadDatabase();
            if (fromDatabase is not null)
            {
                return fromDatabase;
            }

            // A database with no credential table is an older install; only then is auth.json read.
            // An empty table is a real "signed out" state and must not fall back to a stale file.
            if (HasCredentialTable())
            {
                return OpenCodeCredentialLookup.None;
            }
        }

        return this.ReadLegacyAuthFile();
    }

    private bool HasCredentialTable()
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'credential' LIMIT 1;";
            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private OpenCodeCredentialLookup? ReadDatabase()
    {
        try
        {
            using var connection = Open();
            if (!HasTable(connection, "credential"))
            {
                return null;
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT integration_id, value FROM credential "
                + "WHERE active = 1 AND integration_id IN ('opencode-go', 'opencode') "
                + "ORDER BY CASE integration_id WHEN 'opencode-go' THEN 0 ELSE 1 END LIMIT 2;";
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return OpenCodeCredentialLookup.None;
            }

            var integration = reader.GetString(0);
            var payload = reader.IsDBNull(1) ? null : reader.GetValue(1) switch
            {
                string text => text,
                byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
                _ => null,
            };
            if (payload is null)
            {
                return Invalid("OpenCode's saved sign-in is unreadable.");
            }

            // Two active rows for the same integration are ambiguous; never pick one arbitrarily.
            if (reader.Read() && string.Equals(reader.GetString(0), integration, StringComparison.Ordinal))
            {
                return Invalid(
                    "OpenCode has more than one active sign-in for the same integration. "
                    + "Run `opencode auth logout` and sign in again.");
            }

            return ParseCredential(payload, integration);
        }
        catch (SqliteException)
        {
            return OpenCodeCredentialLookup.None;
        }
    }

    private OpenCodeCredentialLookup ParseCredential(string payload, string integration)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return Invalid("OpenCode's saved sign-in is unreadable.");
            }

            return typeElement.GetString() switch
            {
                "key" => KeyCredential(root),
                "oauth" => OAuthCredential(root),
                _ => Invalid("OpenCode's saved sign-in uses an unsupported method."),
            };
        }
        catch (JsonException)
        {
            return Invalid("OpenCode's saved sign-in is unreadable.");
        }
    }

    private static OpenCodeCredentialLookup KeyCredential(JsonElement root)
    {
        var key = root.TryGetProperty("key", out var element) && element.ValueKind == JsonValueKind.String
            ? OpenCodeCredential.Normalise(element.GetString())
            : null;
        return key is null
            ? Invalid("OpenCode's saved API key is empty.")
            : OpenCodeCredentialLookup.Found(new OpenCodeCredential(key, OpenCodeCredentialKind.ApiKey));
    }

    private OpenCodeCredentialLookup OAuthCredential(JsonElement root)
    {
        var token = root.TryGetProperty("access", out var element) && element.ValueKind == JsonValueKind.String
            ? OpenCodeCredential.Normalise(element.GetString())
            : null;
        if (token is null)
        {
            return Invalid("OpenCode's saved sign-in is incomplete.");
        }

        if (!root.TryGetProperty("expires", out var expiryElement)
            || expiryElement.ValueKind != JsonValueKind.Number
            || !expiryElement.TryGetDouble(out var milliseconds)
            || !double.IsFinite(milliseconds)
            || milliseconds <= 0)
        {
            return Invalid("OpenCode's saved sign-in has no usable expiry. Run `opencode auth login`.");
        }

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds);
        if (expiresAt <= this.clock())
        {
            return OpenCodeCredentialLookup.Failed(ProviderError.AuthenticationRequired(
                "The OpenCode sign-in has expired. Run `opencode auth login` and try again."));
        }

        return OpenCodeCredentialLookup.Found(
            new OpenCodeCredential(token, OpenCodeCredentialKind.OAuth, expiresAt));
    }

    private OpenCodeCredentialLookup ReadLegacyAuthFile()
    {
        if (!File.Exists(this.authFilePath))
        {
            return OpenCodeCredentialLookup.None;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(this.authFilePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("opencode-go", out var entry)
                || entry.ValueKind != JsonValueKind.Object)
            {
                return OpenCodeCredentialLookup.None;
            }

            if (!entry.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "api", StringComparison.Ordinal))
            {
                return OpenCodeCredentialLookup.None;
            }

            var key = entry.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.String
                ? OpenCodeCredential.Normalise(keyElement.GetString())
                : null;
            return key is null
                ? Invalid("OpenCode's saved API key is empty.")
                : OpenCodeCredentialLookup.Found(new OpenCodeCredential(key, OpenCodeCredentialKind.ApiKey));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return OpenCodeCredentialLookup.None;
        }
    }

    private SqliteConnection Open()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static bool HasTable(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static OpenCodeCredentialLookup Invalid(string message) =>
        OpenCodeCredentialLookup.Failed(new ProviderError(ProviderErrorKind.AuthenticationRequired, message));
}
