namespace CodexBar.Core.Providers.Copilot;

/// <summary>Copilot provider: GitHub token discovery plus the Copilot usage endpoint.</summary>
public sealed class CopilotProvider : IUsageProvider
{
    private readonly CopilotTokenSource tokenSource;
    private readonly CopilotUsageClient client;

    public CopilotProvider(CopilotTokenSource tokenSource, CopilotUsageClient client)
    {
        this.tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Copilot, "Copilot");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var token = await this.tokenSource.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new UsageFetchResult.Failure(ProviderError.NotConfigured(
                "GitHub token not found. Run `gh auth login`, or save a token in CodexBar's provider setup."));
        }

        return await this.client.FetchAsync(token, cancellationToken).ConfigureAwait(false);
    }
}
