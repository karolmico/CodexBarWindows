using System.IO;
using System.Security.Cryptography;
using System.Text;
using CodexBar.Core.Credentials;
using CodexBar.Core.Providers;
using CodexBar.Providers.Windows;
using Xunit;

namespace CodexBar.Providers.Windows.Tests;

public sealed class DpapiDataProtectorTests
{
    [Fact]
    public void Round_trips_a_provider_token_for_the_current_user()
    {
        var protector = new DpapiDataProtector();
        var plaintext = Encoding.UTF8.GetBytes("ghp_round_trip_value");

        var ciphertext = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Ciphertext_is_not_deterministic()
    {
        var protector = new DpapiDataProtector();
        var plaintext = Encoding.UTF8.GetBytes("ghp_same_input");

        Assert.NotEqual(protector.Protect(plaintext), protector.Protect(plaintext));
    }

    [Fact]
    public void A_tampered_blob_is_rejected()
    {
        var protector = new DpapiDataProtector();
        var ciphertext = protector.Protect(Encoding.UTF8.GetBytes("ghp_tamper_me"));
        ciphertext[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(ciphertext));
    }

    [Fact]
    public void The_store_round_trips_a_token_through_real_dpapi()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codexbar-dpapi-tests", Guid.NewGuid().ToString("n"));
        var path = Path.Combine(directory, "provider-credentials.dat");
        try
        {
            var first = new FileProviderTokenStore(path, new DpapiDataProtector());
            first.SaveToken(ProviderKeys.Copilot, "ghp_on_disk");

            var second = new FileProviderTokenStore(path, new DpapiDataProtector());

            Assert.Equal("ghp_on_disk", second.GetToken(ProviderKeys.Copilot));
            Assert.DoesNotContain("ghp_on_disk", File.ReadAllText(path), StringComparison.Ordinal);

            second.ClearToken(ProviderKeys.Copilot);
            Assert.Null(first.GetToken(ProviderKeys.Copilot));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
