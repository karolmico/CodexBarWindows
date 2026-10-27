namespace CodexBar.Core.Providers.Codex;

/// <summary>Codex provider: credentials from the CLI's auth file, usage from the ChatGPT backend.</summary>
public sealed class CodexProvider : IUsageProvider
{
    private readonly CodexAuthFileStore authStore;
    private readonly CodexUsageClient client;

    public CodexProvider(CodexAuthFileStore authStore, CodexUsageClient client)
    {
        this.authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Codex, "Codex");

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
