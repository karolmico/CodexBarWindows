namespace CodexBar.Core.Providers.Antigravity;

/// <summary>
/// Antigravity provider: finds the local language server and asks it for quota. The client never
/// touches the app's database or credentials; only the loopback probe is used.
/// </summary>
public sealed class AntigravityProvider : IUsageProvider
{
    private readonly IAntigravityProcessSource processSource;
    private readonly AntigravityUsageClient client;

    public AntigravityProvider(IAntigravityProcessSource processSource, AntigravityUsageClient client)
    {
        this.processSource = processSource ?? throw new ArgumentNullException(nameof(processSource));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Antigravity, "Antigravity");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var processes = await this.processSource.ListProcessesAsync(cancellationToken).ConfigureAwait(false);
        var endpoints = processes
            .Select(AntigravityCommandLine.Parse)
            .OfType<AntigravityEndpoint>()
            .ToList();

        if (endpoints.Count == 0)
        {
            return Failure(ProviderError.NotConfigured(
                "Antigravity isn't running. Launch Antigravity and refresh."));
        }

        var tokenless = endpoints.Where(static endpoint =>
            endpoint.Kind is AntigravityProcessKind.App or AntigravityProcessKind.Ide
            && string.IsNullOrEmpty(endpoint.CsrfToken)).ToList();
        if (tokenless.Count == endpoints.Count)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                "Antigravity is running but its local token was not found. Restart Antigravity and try again."));
        }

        UsageFetchResult? last = null;
        foreach (var endpoint in endpoints.Except(tokenless))
        {
            var ports = await this.processSource
                .ListeningPortsAsync(endpoint.Pid, cancellationToken)
                .ConfigureAwait(false);

            var result = await this.client
                .FetchAsync(endpoint, ports, cancellationToken)
                .ConfigureAwait(false);
            if (result is UsageFetchResult.Success)
            {
                return result;
            }

            last = result;
        }

        return last ?? Failure(new ProviderError(
            ProviderErrorKind.Unknown,
            "Antigravity did not return quota. Restart it and try again."));
    }

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);
}
