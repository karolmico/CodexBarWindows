using CodexBar.Core.Identity;
using CodexBar.Core.Persistence;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class SnapshotCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_cache_loads_empty()
    {
        var cache = new SnapshotCache(@"C:\AppData\CodexBar\snapshots.json", _ => null, (_, _) => { });

        Assert.Empty(cache.Load());
    }

    [Fact]
    public void Corrupt_cache_loads_empty_instead_of_throwing()
    {
        var cache = new SnapshotCache(@"C:\AppData\CodexBar\snapshots.json", _ => "{ broken", (_, _) => { });

        Assert.Empty(cache.Load());
    }

    [Fact]
    public void Snapshot_round_trip_preserves_windows_identity_and_flags()
    {
        var files = new Dictionary<string, string>();
        var cache = new SnapshotCache(
            @"C:\AppData\CodexBar\snapshots.json",
            path => files.TryGetValue(path, out var text) ? text : null,
            (path, content) => files[path] = content);

        var original = new UsageSnapshot(
            ProviderKeys.Codex,
            Now,
            new RateWindow(42.5, TimeSpan.FromHours(5), Now.AddHours(3), title: "Session"),
            new RateWindow(87, TimeSpan.FromDays(7), Now.AddDays(2), resetDescription: "Resets Tuesday"),
            [
                new NamedRateWindow("spark", "Codex Spark", new RateWindow(12, TimeSpan.FromHours(5), Now.AddHours(1)), usageKnown: false),
                new NamedRateWindow("placeholder", "Placeholder", new RateWindow(0, isSyntheticPlaceholder: true)),
            ],
            new ProviderIdentity("Plus", "user@example.com", "Acme"));

        cache.Save([original]);
        var loaded = Assert.Single(cache.Load());

        Assert.Equal("codex", loaded.Provider.Value);
        Assert.Equal(Now, loaded.UpdatedAt);
        Assert.Equal(42.5, loaded.Primary!.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), loaded.Primary.Window);
        Assert.Equal("Session", loaded.Primary.Title);
        Assert.Equal("Resets Tuesday", loaded.Secondary!.ResetDescription);
        Assert.Equal(2, loaded.ExtraWindows.Count);
        Assert.False(loaded.ExtraWindows[0].UsageKnown);
        Assert.True(loaded.ExtraWindows[1].Window.IsSyntheticPlaceholder);
        Assert.Equal("Plus", loaded.Identity!.PlanName);
        Assert.Equal("user@example.com", loaded.Identity.AccountLabel);
        Assert.Equal("Acme", loaded.Identity.Organization);
    }

    [Fact]
    public void Malformed_entry_is_dropped_without_discarding_valid_siblings()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "snapshots": [
                { "provider": "NOT A KEY", "updatedAt": "2026-10-27T12:00:00+00:00" },
                { "provider": "claude", "updatedAt": "2026-10-27T11:00:00+00:00",
                  "primary": { "usedPercent": 61 } }
              ]
            }
            """;
        var cache = new SnapshotCache(@"C:\AppData\CodexBar\snapshots.json", _ => json, (_, _) => { });

        var loaded = cache.Load();

        var snapshot = Assert.Single(loaded);
        Assert.Equal("claude", snapshot.Provider.Value);
        Assert.Equal(61d, snapshot.Primary!.UsedPercent);
    }

    [Fact]
    public void Saving_an_empty_set_clears_the_cache_file()
    {
        var written = string.Empty;
        var cache = new SnapshotCache(@"C:\AppData\CodexBar\snapshots.json", _ => null, (_, content) => written = content);

        cache.Save([]);

        Assert.Contains("\"snapshots\": []", written, StringComparison.Ordinal);
    }
}
