namespace CodexBar.Core.Providers.OpenCode;

/// <summary>
/// OpenCode provider: reports the OpenCode Go subscription meters (5-hour, weekly, monthly) from
/// the public usage API. The credential comes from CodexBar's encrypted store, the environment, or
/// the sign-in the OpenCode CLI already saved, so an existing CLI login needs no extra setup.
/// </summary>
public sealed class OpenCodeProvider : IUsageProvider
{
    private readonly OpenCodeCredentialResolver resolver;
    private readonly OpenCodeUsageClient client;

    public OpenCodeProvider(OpenCodeCredentialResolver resolver, OpenCodeUsageClient client)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.OpenCode, "OpenCode");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var lookup = await this.resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (lookup.Error is { } error)
        {
            return new UsageFetchResult.Failure(error);
        }

        if (lookup.Credential is not { } credential)
        {
            return new UsageFetchResult.Failure(ProviderError.NotConfigured(
                "OpenCode credential not found. Run `opencode auth login`, or save an API key in CodexBar's provider setup."));
        }

        return await this.client.FetchAsync(credential, cancellationToken).ConfigureAwait(false);
    }
}
