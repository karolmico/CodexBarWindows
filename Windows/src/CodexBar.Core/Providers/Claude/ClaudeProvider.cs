namespace CodexBar.Core.Providers.Claude;

/// <summary>Claude provider: credentials from the CLI's credential file, usage from the OAuth endpoint.</summary>
public sealed class ClaudeProvider : IUsageProvider
{
    private readonly ClaudeAuthFileStore authStore;
    private readonly ClaudeUsageClient client;

    public ClaudeProvider(ClaudeAuthFileStore authStore, ClaudeUsageClient client)
    {
        this.authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Claude, "Claude");

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
