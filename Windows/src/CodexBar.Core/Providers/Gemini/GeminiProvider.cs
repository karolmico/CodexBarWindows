namespace CodexBar.Core.Providers.Gemini;

/// <summary>Gemini provider: credentials from the CLI file, quota from the Cloud Code private API.</summary>
public sealed class GeminiProvider : IUsageProvider
{
    private readonly GeminiAuthFileStore authStore;
    private readonly GeminiUsageClient client;

    public GeminiProvider(GeminiAuthFileStore authStore, GeminiUsageClient client)
    {
        this.authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Gemini, "Gemini");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var auth = this.authStore.Load();
        if (auth.Credentials is null)
        {
            return new UsageFetchResult.Failure(auth.Error!);
        }

        return await this.client.FetchAsync(auth.Credentials, cancellationToken).ConfigureAwait(false);
    }
}
