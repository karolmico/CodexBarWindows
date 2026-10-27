namespace CodexBar.Core.Providers.Cursor;

/// <summary>Cursor provider: local app session plus the Cursor web usage summary.</summary>
public sealed class CursorProvider : IUsageProvider
{
    private readonly CursorSessionStore sessionStore;
    private readonly CursorUsageClient client;

    public CursorProvider(CursorSessionStore sessionStore, CursorUsageClient client)
    {
        this.sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Cursor, "Cursor");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var auth = this.sessionStore.Load();
        if (auth.Session is null)
        {
            return new UsageFetchResult.Failure(auth.Error!);
        }

        return await this.client.FetchAsync(auth.Session, cancellationToken).ConfigureAwait(false);
    }
}
