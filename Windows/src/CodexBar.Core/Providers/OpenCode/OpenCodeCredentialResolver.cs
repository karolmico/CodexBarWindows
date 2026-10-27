using CodexBar.Core.Credentials;

namespace CodexBar.Core.Providers.OpenCode;

/// <summary>
/// Finds an OpenCode credential the way upstream orders its sources: a key saved in CodexBar wins,
/// then <c>OPENCODE_API_KEY</c>, then the sign-in the OpenCode CLI already stored. Both an API key
/// and the CLI's OAuth access token work with the Go usage endpoint.
/// </summary>
public sealed class OpenCodeCredentialResolver
{
    private readonly IProviderTokenStore? tokenStore;
    private readonly IOpenCodeCredentialSource cliSource;
    private readonly Func<IReadOnlyDictionary<string, string>> environment;

    public OpenCodeCredentialResolver(
        IOpenCodeCredentialSource cliSource,
        IProviderTokenStore? tokenStore = null,
        Func<IReadOnlyDictionary<string, string>>? environment = null)
    {
        this.cliSource = cliSource ?? throw new ArgumentNullException(nameof(cliSource));
        this.tokenStore = tokenStore;
        this.environment = environment ?? ReadEnvironment;
    }

    public async Task<OpenCodeCredentialLookup> ResolveAsync(CancellationToken cancellationToken)
    {
        var stored = OpenCodeCredential.Normalise(this.tokenStore?.GetToken(ProviderKeys.OpenCode));
        if (stored is not null)
        {
            return OpenCodeCredentialLookup.Found(
                new OpenCodeCredential(stored, OpenCodeCredentialKind.ApiKey));
        }

        var environment = this.environment();
        if (environment.TryGetValue("OPENCODE_API_KEY", out var value)
            && OpenCodeCredential.Normalise(value) is { } fromEnvironment)
        {
            return OpenCodeCredentialLookup.Found(
                new OpenCodeCredential(fromEnvironment, OpenCodeCredentialKind.ApiKey));
        }

        return await this.cliSource.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "OPENCODE_API_KEY" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return values;
    }
}
