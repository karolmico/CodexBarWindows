using System.IO;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.OpenCode;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexBar.Core.Tests.OpenCode;

public sealed class OpenCodeCliCredentialStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "codexbar-opencode-" + Guid.NewGuid().ToString("N"));

    public OpenCodeCliCredentialStoreTests() => Directory.CreateDirectory(this.directory);

    private string Database => Path.Combine(this.directory, "opencode.db");

    private string AuthFile => Path.Combine(this.directory, "auth.json");

    [Fact]
    public async Task An_active_api_key_row_is_read_from_the_database()
    {
        this.CreateDatabase();
        this.InsertCredential("opencode-go", """{"type":"key","key":"sk-from-database"}""", active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Error);
        Assert.Equal("sk-from-database", lookup.Credential!.Token);
        Assert.Equal(OpenCodeCredentialKind.ApiKey, lookup.Credential.Kind);
    }

    [Fact]
    public async Task The_generic_login_is_used_when_no_go_row_exists()
    {
        this.CreateDatabase();
        this.InsertCredential(
            "opencode",
            $$"""{"type":"oauth","access":"oauth-access","expires":{{Now.AddDays(7).ToUnixTimeMilliseconds()}}}""",
            active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Equal("oauth-access", lookup.Credential!.Token);
        Assert.Equal(OpenCodeCredentialKind.OAuth, lookup.Credential.Kind);
        Assert.Equal(Now.AddDays(7), lookup.Credential.ExpiresAt);
    }

    [Fact]
    public async Task The_go_row_wins_over_the_generic_login()
    {
        this.CreateDatabase();
        this.InsertCredential(
            "opencode",
            $$"""{"type":"oauth","access":"generic","expires":{{Now.AddDays(7).ToUnixTimeMilliseconds()}}}""",
            active: 1);
        this.InsertCredential("opencode-go", """{"type":"key","key":"go-key"}""", active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Equal("go-key", lookup.Credential!.Token);
    }

    [Fact]
    public async Task An_expired_oauth_login_asks_for_a_new_sign_in()
    {
        this.CreateDatabase();
        this.InsertCredential(
            "opencode",
            $$"""{"type":"oauth","access":"old","expires":{{Now.AddDays(-1).ToUnixTimeMilliseconds()}}}""",
            active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, lookup.Error!.Kind);
        Assert.Contains("opencode auth login", lookup.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inactive_rows_are_ignored()
    {
        this.CreateDatabase();
        this.InsertCredential("opencode-go", """{"type":"key","key":"sk-inactive"}""", active: 0);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Null(lookup.Error);
    }

    [Fact]
    public async Task Two_active_rows_for_the_same_integration_are_ambiguous()
    {
        this.CreateDatabase();
        this.InsertCredential("opencode-go", """{"type":"key","key":"first"}""", active: 1);
        this.InsertCredential("opencode-go", """{"type":"key","key":"second"}""", active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Contains("more than one active sign-in", lookup.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first", lookup.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_credential_table_does_not_fall_back_to_the_legacy_file()
    {
        this.CreateDatabase();
        File.WriteAllText(this.AuthFile, """{"opencode-go":{"type":"api","key":"stale-legacy"}}""");

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Null(lookup.Error);
    }

    [Fact]
    public async Task A_legacy_auth_file_provides_the_go_key_when_no_database_exists()
    {
        File.WriteAllText(this.AuthFile, """{"opencode-go":{"type":"api","key":"legacy-key"}}""");

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Equal("legacy-key", lookup.Credential!.Token);
        Assert.Equal(OpenCodeCredentialKind.ApiKey, lookup.Credential.Kind);
    }

    [Fact]
    public async Task A_missing_store_is_simply_no_credential()
    {
        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Null(lookup.Error);
    }

    [Fact]
    public async Task A_corrupt_credential_payload_asks_for_a_new_sign_in()
    {
        this.CreateDatabase();
        this.InsertCredential("opencode-go", "not json", active: 1);

        var lookup = await this.Store().ReadAsync(CancellationToken.None);

        Assert.Null(lookup.Credential);
        Assert.Equal(ProviderErrorKind.AuthenticationRequired, lookup.Error!.Kind);
    }

    private OpenCodeCliCredentialStore Store() => new(this.Database, this.AuthFile, () => Now);

    private void CreateDatabase()
    {
        using var connection = new SqliteConnection("Data Source=" + this.Database);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE credential (id TEXT PRIMARY KEY, integration_id TEXT NOT NULL, "
            + "value TEXT, active INTEGER NOT NULL DEFAULT 0);";
        command.ExecuteNonQuery();
    }

    private void InsertCredential(string integration, string value, int active)
    {
        using var connection = new SqliteConnection("Data Source=" + this.Database);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO credential (id, integration_id, value, active) VALUES ($id, $integration, $value, $active);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$integration", integration);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$active", active);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.directory, recursive: true);
        }
        catch (IOException)
        {
            // A lingering SQLite handle must not fail the suite.
        }
    }
}
