using CodexBar.Core.Providers;

namespace CodexBar.Core.Credentials;

/// <summary>
/// Encrypts a small byte payload at rest. The Windows implementation is DPAPI scoped to the current
/// user; tests use a deterministic fake so no test ever needs a real key.
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>
/// Explicit token overrides the user saved in CodexBar. Only providers that can actually use a token
/// have entries here; the file is the sole place this app writes a credential.
/// </summary>
public interface IProviderTokenStore
{
    /// <summary>The saved token for a provider, or null when none is stored or the store is unreadable.</summary>
    string? GetToken(ProviderKey provider);

    /// <summary>True when a usable token is stored; never reads the token into a log.</summary>
    bool HasToken(ProviderKey provider) => this.GetToken(provider) is not null;

    void SaveToken(ProviderKey provider, string token);

    void ClearToken(ProviderKey provider);
}
