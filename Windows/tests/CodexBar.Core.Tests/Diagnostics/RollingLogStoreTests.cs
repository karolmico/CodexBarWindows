using System.IO;
using CodexBar.Core.Diagnostics;
using Xunit;

namespace CodexBar.Core.Tests.Diagnostics;

public sealed class RollingLogStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Writes_one_redacted_line_per_entry_and_creates_the_directory()
    {
        using var temp = new TempDirectory();
        var directory = Path.Combine(temp.Path, "logs");
        var store = new RollingLogStore(directory, clock: () => Now);

        store.Warn("codex failed with token=abc123 for dev@example.com at C:\\Users\\dev\\auth.json");

        var content = File.ReadAllText(store.CurrentPath);
        Assert.Contains("2026-10-27T12:00:00.000Z [WARN]", content, StringComparison.Ordinal);
        Assert.Contains("[redacted]", content, StringComparison.Ordinal);
        Assert.Contains("[email]", content, StringComparison.Ordinal);
        Assert.Contains("[path]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", content, StringComparison.Ordinal);
        Assert.DoesNotContain("dev@example.com", content, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(store.CurrentPath));
    }

    [Fact]
    public void Exceptions_are_recorded_by_type_and_message_but_stay_on_one_line()
    {
        using var temp = new TempDirectory();
        var store = new RollingLogStore(temp.Path, clock: () => Now);

        store.Error("refresh failed", new InvalidOperationException("line one\r\nline two"));

        var lines = File.ReadAllLines(store.CurrentPath);
        Assert.Single(lines);
        Assert.Contains("InvalidOperationException: line one  line two", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Rolls_at_the_size_cap_and_prunes_older_files()
    {
        using var temp = new TempDirectory();
        var store = new RollingLogStore(temp.Path, maxBytes: 400, maxFiles: 2, clock: () => Now);

        for (var index = 0; index < 60; index++)
        {
            store.Info($"entry {index:00} with enough padding to fill the file");
        }

        Assert.True(File.Exists(store.CurrentPath));
        Assert.True(File.Exists(store.CurrentPath + ".1"));
        Assert.True(File.Exists(store.CurrentPath + ".2"));
        Assert.False(File.Exists(store.CurrentPath + ".3"));
        Assert.True(new FileInfo(store.CurrentPath).Length <= 400);
        Assert.True(new FileInfo(store.CurrentPath + ".1").Length <= 400);
    }

    [Fact]
    public void A_failure_to_write_is_swallowed()
    {
        using var temp = new TempDirectory();
        var blocked = Path.Combine(temp.Path, "file-not-a-directory");
        File.WriteAllText(blocked, "occupied");
        var store = new RollingLogStore(blocked);

        store.Info("this must not throw");

        Assert.True(File.Exists(blocked));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            this.Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "codexbar-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
