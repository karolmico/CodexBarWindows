using System.IO;
using Microsoft.Data.Sqlite;

namespace CodexBar.Core.Providers.Cursor;

/// <summary>
/// Reads the single <c>cursorAuth/accessToken</c> row from Cursor's VS Code state database. The
/// database is opened read-only and no other table or browser store is touched.
/// </summary>
public static class CursorStateDatabase
{
    public const string AccessTokenKey = "cursorAuth/accessToken";

    /// <summary>Returns the stored token, or null when the database or row is absent.</summary>
    public static string? ReadAccessToken(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", AccessTokenKey);
            return command.ExecuteScalar() switch
            {
                string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                byte[] bytes => DecodeBytes(bytes),
                _ => null,
            };
        }
        catch (SqliteException)
        {
            // A locked or half-written database must not take the tray down; the user can retry.
            return null;
        }
    }

    private static string? DecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        // VS Code has stored some values as UTF-16LE; ASCII UTF-16 is also valid UTF-8 with NULs, so
        // detect it before falling back.
        if (bytes.Length % 2 == 0 && bytes.Where((_, index) => index % 2 == 1).All(static value => value == 0))
        {
            var utf16 = System.Text.Encoding.Unicode.GetString(bytes);
            if (!string.IsNullOrWhiteSpace(utf16))
            {
                return utf16.Trim();
            }
        }

        var utf8 = System.Text.Encoding.UTF8.GetString(bytes);
        return string.IsNullOrWhiteSpace(utf8) ? null : utf8.Trim();
    }
}
