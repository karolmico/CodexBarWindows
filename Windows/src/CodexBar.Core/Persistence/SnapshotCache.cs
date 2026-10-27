using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexBar.Core.Identity;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Persistence;

/// <summary>
/// Stores the last good snapshot per provider so a restart shows numbers immediately while the first
/// refresh runs. The cache is disposable: unreadable entries are dropped, never surfaced as provider errors.
/// </summary>
public sealed class SnapshotCache
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string path;
    private readonly Func<string, string?> readAllText;
    private readonly Action<string, string> writeAllText;

    public SnapshotCache(
        string path,
        Func<string, string?>? readAllText = null,
        Action<string, string>? writeAllText = null)
    {
        this.path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A cache path is required.", nameof(path))
            : path;
        this.readAllText = readAllText ?? ReadFileOrNull;
        this.writeAllText = writeAllText ?? WriteFileAtomically;
    }

    public string Path => this.path;

    public IReadOnlyList<UsageSnapshot> Load()
    {
        string? text;
        try
        {
            text = this.readAllText(this.path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (text is null)
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<CacheDocument>(text, JsonOptions);
            if (document?.Snapshots is null)
            {
                return [];
            }

            var snapshots = new List<UsageSnapshot>();
            foreach (var entry in document.Snapshots)
            {
                if (TryMap(entry) is { } snapshot)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Replaces the cache with the supplied snapshots; providers without a snapshot are omitted.</summary>
    public void Save(IEnumerable<UsageSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var document = new CacheDocument(
            CurrentSchemaVersion,
            snapshots.Select(Map).ToList());

        this.writeAllText(this.path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static SnapshotEntry Map(UsageSnapshot snapshot) => new(
        snapshot.Provider.Value,
        snapshot.UpdatedAt,
        MapWindow(snapshot.Primary),
        MapWindow(snapshot.Secondary),
        snapshot.ExtraWindows.Select(static window => new NamedWindowEntry(
            window.Id,
            window.Title,
            MapWindow(window.Window)!,
            window.UsageKnown)).ToList(),
        snapshot.Identity is { } identity
            ? new IdentityEntry(identity.PlanName, identity.AccountLabel, identity.Organization)
            : null);

    private static WindowEntry? MapWindow(RateWindow? window) => window is null
        ? null
        : new WindowEntry(
            window.UsedPercent,
            window.Window?.TotalMinutes,
            window.ResetsAt,
            window.ResetDescription,
            window.Title,
            window.QuotaLimit,
            window.QuotaUnit,
            window.IsSyntheticPlaceholder);

    private static UsageSnapshot? TryMap(SnapshotEntry entry)
    {
        if (!ProviderKey.TryParse(entry.Provider, out var provider))
        {
            return null;
        }

        try
        {
            var extras = (entry.ExtraWindows ?? [])
                .Select(static window => new NamedRateWindow(
                    window.Id ?? string.Empty,
                    window.Title ?? string.Empty,
                    MapWindow(window.Window)!,
                    window.UsageKnown))
                .ToArray();

            return new UsageSnapshot(
                provider,
                entry.UpdatedAt,
                MapWindow(entry.Primary),
                MapWindow(entry.Secondary),
                extras,
                entry.Identity is { } identity
                    ? new ProviderIdentity(identity.PlanName, identity.AccountLabel, identity.Organization)
                    : null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static RateWindow? MapWindow(WindowEntry? entry) => entry is null
        ? null
        : new RateWindow(
            entry.UsedPercent,
            entry.WindowMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            entry.ResetsAt,
            entry.ResetDescription,
            entry.Title,
            entry.QuotaLimit,
            entry.QuotaUnit,
            entry.IsSyntheticPlaceholder);

    private static string? ReadFileOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

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

    private sealed record CacheDocument(int SchemaVersion, List<SnapshotEntry>? Snapshots);

    private sealed record SnapshotEntry(
        string? Provider,
        DateTimeOffset UpdatedAt,
        WindowEntry? Primary,
        WindowEntry? Secondary,
        List<NamedWindowEntry>? ExtraWindows,
        IdentityEntry? Identity);

    private sealed record WindowEntry(
        double UsedPercent,
        double? WindowMinutes,
        DateTimeOffset? ResetsAt,
        string? ResetDescription,
        string? Title,
        double? QuotaLimit,
        string? QuotaUnit,
        bool IsSyntheticPlaceholder);

    private sealed record NamedWindowEntry(
        string? Id,
        string? Title,
        WindowEntry? Window,
        bool UsageKnown);

    private sealed record IdentityEntry(string? PlanName, string? AccountLabel, string? Organization);
}
