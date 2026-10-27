using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Settings;

/// <summary>Settings plus any warning the user should see about a recovered file.</summary>
public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

/// <summary>
/// Reads and writes the JSON settings file. A corrupt file is backed up and replaced by defaults with a
/// warning; a file from a newer schema is left untouched so a downgrade cannot destroy the user's config.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string path;
    private readonly Func<string, string?> readAllText;
    private readonly Action<string, string> writeAllText;
    private readonly Func<DateTimeOffset> clock;

    public SettingsStore(
        string path,
        Func<string, string?>? readAllText = null,
        Action<string, string>? writeAllText = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A settings path is required.", nameof(path))
            : path;
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.writeAllText = writeAllText ?? WriteFileAtomically;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Path => this.path;

    public SettingsLoadResult Load(AppSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        string? text;
        try
        {
            text = this.readAllText(this.path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(
                defaults,
                $"Settings could not be read ({exception.Message}). Defaults are in use.");
        }

        if (text is null)
        {
            return new SettingsLoadResult(defaults, Warning: null);
        }

        try
        {
            var document = JsonSerializer.Deserialize<SettingsDocument>(text, JsonOptions);
            if (document is null)
            {
                return this.Recover(text, defaults, "Settings were empty.");
            }

            if (document.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                return new SettingsLoadResult(
                    defaults,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Settings use schema {document.SchemaVersion}, newer than this build ({AppSettings.CurrentSchemaVersion}). Defaults are in use and the file was left untouched."));
            }

            var providers = (document.Providers ?? [])
                .Select(entry => (entry.Key, entry.Enabled))
                .Where(static entry => ProviderKey.TryParse(entry.Key, out _))
                .Select(static entry => new ProviderPreference(ProviderKey.Parse(entry.Key!), entry.Enabled))
                .ToArray();

            var settings = new AppSettings(
                providers.Length == 0 ? defaults.Providers : providers,
                TimeSpan.FromMinutes(document.RefreshFloorMinutes ?? defaults.RefreshFloor.TotalMinutes),
                document.SchemaVersion,
                document.NotificationThresholds ?? defaults.NotificationThresholds,
                document.NotifyResets ?? defaults.NotifyResets,
                ParseTime(document.QuietHoursStart) ?? defaults.QuietHoursStart,
                ParseTime(document.QuietHoursEnd) ?? defaults.QuietHoursEnd);

            return new SettingsLoadResult(settings, Warning: null);
        }
        catch (JsonException exception)
        {
            return this.Recover(text, defaults, exception.Message);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var document = new SettingsDocument(
            AppSettings.CurrentSchemaVersion,
            settings.Providers.Select(static preference => new ProviderEntry(
                preference.Key.Value,
                preference.Enabled)).ToList(),
            settings.RefreshFloor.TotalMinutes,
            settings.NotificationThresholds.ToList(),
            settings.NotifyResets,
            FormatTime(settings.QuietHoursStart),
            FormatTime(settings.QuietHoursEnd));

        this.writeAllText(this.path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private SettingsLoadResult Recover(string originalText, AppSettings defaults, string reason)
    {
        var backupPath = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.path}.corrupt-{this.clock().ToUnixTimeSeconds()}");
        try
        {
            this.writeAllText(backupPath, originalText);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(
                defaults,
                $"Settings were unreadable ({reason}) and could not be backed up. Defaults are in use.");
        }

        return new SettingsLoadResult(
            defaults,
            $"Settings were unreadable ({reason}). Defaults are in use; the previous file was saved as {backupPath}.");
    }

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private static TimeOnly? ParseTime(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && TimeOnly.TryParseExact(
            value.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static string? FormatTime(TimeOnly? value) =>
        value?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static void WriteFileAtomically(string path, string content)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed record SettingsDocument(
        int SchemaVersion,
        List<ProviderEntry>? Providers,
        double? RefreshFloorMinutes,
        List<int>? NotificationThresholds,
        bool? NotifyResets,
        string? QuietHoursStart,
        string? QuietHoursEnd);

    private sealed record ProviderEntry(string? Key, bool Enabled);
}
