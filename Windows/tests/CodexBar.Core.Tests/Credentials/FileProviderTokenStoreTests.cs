using System.IO;
using System.Security.Cryptography;
using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using Xunit;

namespace CodexBar.Core.Tests.Credentials;

public sealed class FileProviderTokenStoreTests
{
    private const string Path = @"C:\fake\provider-credentials.dat";

    [Fact]
    public void A_saved_token_round_trips_through_the_protector()
    {
        var store = new MemoryFile().Store(new XorProtector());

        store.SaveToken(ProviderKeys.Copilot, "  ghp_example  ");

        Assert.Equal("ghp_example", store.GetToken(ProviderKeys.Copilot));
        Assert.True(store.HasToken(ProviderKeys.Copilot));
    }

    [Fact]
    public void The_file_never_contains_the_token_in_plain_text()
    {
        string? written = null;
        var store = new FileProviderTokenStore(
            Path,
            new XorProtector(),
            _ => null,
            (_, text) => written = text);

        store.SaveToken(ProviderKeys.Copilot, "ghp_very_secret_value");

        Assert.NotNull(written);
        Assert.DoesNotContain("ghp_very_secret_value", written, StringComparison.Ordinal);
        Assert.Contains("copilot", written, StringComparison.Ordinal);
        Assert.Contains("version", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Clearing_one_provider_keeps_the_others()
    {
        MemoryFile file = new();
        var store = file.Store(new XorProtector());
        store.SaveToken(ProviderKeys.Copilot, "ghp_one");
        store.SaveToken(ProviderKey.Parse("other"), "second");

        store.ClearToken(ProviderKeys.Copilot);

        Assert.False(store.HasToken(ProviderKeys.Copilot));
        Assert.Equal("second", store.GetToken(ProviderKey.Parse("other")));
    }

    [Fact]
    public void Clearing_an_absent_token_does_not_write()
    {
        var writes = 0;
        var store = new FileProviderTokenStore(
            Path,
            new XorProtector(),
            _ => null,
            (_, _) => writes++);

        store.ClearToken(ProviderKeys.Copilot);

        Assert.Equal(0, writes);
    }

    [Fact]
    public void A_missing_file_reads_as_no_token()
    {
        var store = new FileProviderTokenStore(Path, new XorProtector(), _ => null, NoWrite);

        Assert.Null(store.GetToken(ProviderKeys.Copilot));
    }

    [Fact]
    public void A_corrupt_file_fails_closed_and_can_be_replaced()
    {
        MemoryFile file = new() { Text = "{ not json" };
        var store = file.Store(new XorProtector());

        Assert.Null(store.GetToken(ProviderKeys.Copilot));

        store.SaveToken(ProviderKeys.Copilot, "ghp_new");

        Assert.Equal("ghp_new", store.GetToken(ProviderKeys.Copilot));
    }

    [Fact]
    public void An_unreadable_file_reads_as_no_token()
    {
        var store = new FileProviderTokenStore(
            Path,
            new XorProtector(),
            _ => throw new IOException("denied"),
            NoWrite);

        Assert.Null(store.GetToken(ProviderKeys.Copilot));
    }

    [Fact]
    public void A_token_that_cannot_be_decrypted_reads_as_absent()
    {
        MemoryFile file = new();
        var protector = new XorProtector();
        file.Store(protector).SaveToken(ProviderKeys.Copilot, "ghp_saved");

        var store = file.Store(new RejectingProtector());

        Assert.Null(store.GetToken(ProviderKeys.Copilot));
    }

    [Fact]
    public void An_empty_token_is_rejected_without_writing()
    {
        var writes = 0;
        var store = new FileProviderTokenStore(
            Path,
            new XorProtector(),
            _ => null,
            (_, _) => writes++);

        Assert.Throws<ArgumentException>(() => store.SaveToken(ProviderKeys.Copilot, "   "));
        Assert.Equal(0, writes);
    }

    [Fact]
    public void A_write_failure_is_reported_without_leaking_the_token()
    {
        var store = new FileProviderTokenStore(
            Path,
            new XorProtector(),
            _ => null,
            (_, _) => throw new UnauthorizedAccessException("denied"));

        var exception = Assert.Throws<CredentialStoreException>(
            () => store.SaveToken(ProviderKeys.Copilot, "ghp_secret"));

        Assert.DoesNotContain("ghp_secret", exception.Message, StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    [Fact]
    public void The_default_writer_persists_atomically_on_disk()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codexbar-tests", Guid.NewGuid().ToString("n"));
        var path = System.IO.Path.Combine(directory, "provider-credentials.dat");
        try
        {
            var store = new FileProviderTokenStore(path, new XorProtector());
            store.SaveToken(ProviderKeys.Copilot, "ghp_disk");

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal("ghp_disk", new FileProviderTokenStore(path, new XorProtector()).GetToken(ProviderKeys.Copilot));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void NoWrite(string path, string text)
    {
    }

    private sealed class MemoryFile
    {
        public string? Text { get; set; }

        public FileProviderTokenStore Store(IDataProtector protector) =>
            new(
                Path,
                protector,
                _ => this.Text,
                (_, text) => this.Text = text);
    }

    /// <summary>Deterministic stand-in for DPAPI: a marker byte plus a fixed XOR.</summary>
    private sealed class XorProtector : IDataProtector
    {
        private const byte Marker = 0xA5;
        private const byte Key = 0x5A;

        public byte[] Protect(byte[] plaintext) =>
            [Marker, .. plaintext.Select(value => (byte)(value ^ Key))];

        public byte[] Unprotect(byte[] ciphertext)
        {
            if (ciphertext.Length == 0 || ciphertext[0] != Marker)
            {
                throw new CryptographicException("Not a token this protector wrote.");
            }

            return ciphertext.Skip(1).Select(value => (byte)(value ^ Key)).ToArray();
        }
    }

    private sealed class RejectingProtector : IDataProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("no key");

        public byte[] Unprotect(byte[] ciphertext) => throw new CryptographicException("no key");
    }
}
