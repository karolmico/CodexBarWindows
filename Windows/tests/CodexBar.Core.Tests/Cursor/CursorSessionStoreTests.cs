using System.IO;
using System.Text.Json;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Cursor;
using CodexBar.Core.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexBar.Core.Tests.Cursor;

public sealed class CursorSessionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly string UserProfile = @"C:\Users\Test";

    private readonly string root = Path.Combine(Path.GetTempPath(), "codexbar-cursor-" + Guid.NewGuid().ToString("N"));

    public CursorSessionStoreTests() => Directory.CreateDirectory(this.root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless.
        }
    }

    [Fact]
    public void Reads_the_access_token_from_the_cursor_state_database()
    {
        var token = Jwt(
            ("sub", "auth0|user_01TEST"),
            ("email", "user@example.com"),
            ("exp", Now.AddDays(30).ToUnixTimeSeconds()));
        var database = this.CreateStateDatabase(token);

        var store = new CursorSessionStore(this.root, UserProfile, _ => null, clock: () => Now);

        var session = store.Load().Session;

        Assert.NotNull(session);
        Assert.Equal(token, session.AccessToken);
        Assert.Equal("user_01TEST", session.UserId);
        Assert.Equal("user@example.com", session.Email);
        Assert.Equal(database, store.StateDatabasePath());
    }

    [Fact]
    public void Falls_back_to_the_cursor_cli_auth_file()
    {
        var token = Jwt(
            ("sub", "user_01CLI"),
            ("exp", Now.AddDays(5).ToUnixTimeSeconds()));
        var expected = Path.Combine(UserProfile, ".cursor", "auth.json");
        var readPaths = new List<string>();
        var store = new CursorSessionStore(
            this.root,
            UserProfile,
            path =>
            {
                readPaths.Add(path);
                return path == expected ? $"{{\"accessToken\":\"{token}\"}}" : null;
            },
            _ => null,
            () => Now);

        var session = store.Load().Session;

        Assert.Equal("user_01CLI", session!.UserId);
        Assert.Contains(expected, readPaths);
    }

    [Fact]
    public void Missing_installation_reports_not_configured()
    {
        var store = new CursorSessionStore(this.root, UserProfile, _ => null, _ => null, () => Now);

        var result = store.Load();

        Assert.Null(result.Session);
        Assert.Equal(ProviderErrorKind.NotConfigured, result.Error!.Kind);
        Assert.Contains("not found", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installed_but_signed_out_reports_not_configured()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "Cursor", "User", "globalStorage"));
        this.CreateEmptyStateDatabase();
        var store = new CursorSessionStore(this.root, UserProfile, _ => null, clock: () => Now);

        var result = store.Load();

        Assert.Equal(ProviderErrorKind.NotConfigured, result.Error!.Kind);
        Assert.Contains("no account is signed in", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_token_requires_authentication()
    {
        var token = Jwt(
            ("sub", "user_01TEST"),
            ("exp", Now.AddMinutes(-5).ToUnixTimeSeconds()));
        var store = new CursorSessionStore(this.root, UserProfile, _ => null, _ => token, () => Now);

        var result = store.Load();

        Assert.Null(result.Session);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
        Assert.Contains("expired", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Encrypted_token_fails_closed_with_cli_guidance()
    {
        var store = new CursorSessionStore(
            this.root,
            UserProfile,
            _ => null,
            _ => "cursor-safe-storage:v1:AAAA",
            () => Now);

        var result = store.Load();

        Assert.Null(result.Session);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, result.Error!.Kind);
        Assert.Contains("cursor-agent login", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_token_is_a_parse_error()
    {
        var store = new CursorSessionStore(this.root, UserProfile, _ => null, _ => "not-a-jwt", () => Now);

        var result = store.Load();

        Assert.Equal(ProviderErrorKind.Parse, result.Error!.Kind);
    }

    [Fact]
    public void Session_text_never_contains_the_token()
    {
        var token = Jwt(("sub", "user_01TEST"), ("exp", Now.AddDays(30).ToUnixTimeSeconds()));
        var store = new CursorSessionStore(this.root, UserProfile, _ => null, _ => token, () => Now);

        var text = store.Load().Session!.ToString();

        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.Contains("user_01TEST", text, StringComparison.Ordinal);
    }

    private string CreateStateDatabase(string token)
    {
        var database = this.StateDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", CursorStateDatabase.AccessTokenKey);
        command.Parameters.AddWithValue("$value", token);
        command.ExecuteNonQuery();
        return database;
    }

    private string CreateEmptyStateDatabase()
    {
        var database = this.StateDatabasePath();
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);";
        command.ExecuteNonQuery();
        return database;
    }

    private string StateDatabasePath() =>
        Path.Combine(this.root, "Cursor", "User", "globalStorage", "state.vscdb");

    private static string Jwt(params (string Key, object Value)[] claims)
    {
        static string Part(string value) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        var payload = claims.ToDictionary(static claim => claim.Key, static claim => claim.Value);
        return $"{Part("{\"alg\":\"none\"}")}.{Part(JsonSerializer.Serialize(payload))}.";
    }
}
