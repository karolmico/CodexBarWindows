namespace CodexBar.Core.Providers.Antigravity;

/// <summary>
/// Antigravity provider. It prefers the desktop app's local language server (richer identity, no
/// extra process) and falls back to the <c>agy</c> CLI print report when the app is closed or its
/// probe fails. The CLI fallback is what lets the tray show quota with no Antigravity window open.
/// Neither source opens the app's database or credential files.
/// </summary>
public sealed class AntigravityProvider : IUsageProvider
{
    private readonly IAntigravityProcessSource processSource;
    private readonly AntigravityUsageClient client;
    private readonly IAntigravityCliSource? cliSource;

    public AntigravityProvider(
        IAntigravityProcessSource processSource,
        AntigravityUsageClient client,
        IAntigravityCliSource? cliSource = null)
    {
        this.processSource = processSource ?? throw new ArgumentNullException(nameof(processSource));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.cliSource = cliSource;
    }

    public ProviderDescriptor Descriptor { get; } = new(ProviderKeys.Antigravity, "Antigravity");

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var processes = await this.processSource.ListProcessesAsync(cancellationToken).ConfigureAwait(false);
        var endpoints = processes
            .Select(AntigravityCommandLine.Parse)
            .OfType<AntigravityEndpoint>()
            .ToList();

        // Only the app and IDE language servers authenticate local callers with a CSRF token.
        // `agy` sessions started by hand have no token, and every CLI since 1.2.2 rejects
        // tokenless probes, so they are served by the print report instead of a doomed HTTP call.
        var appEndpoints = endpoints
            .Where(static endpoint => endpoint.Kind is AntigravityProcessKind.App or AntigravityProcessKind.Ide)
            .ToList();
        var probes = appEndpoints
            .Where(static endpoint => !string.IsNullOrEmpty(endpoint.CsrfToken))
            .ToList();

        UsageFetchResult? probeFailure = null;
        foreach (var endpoint in probes)
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

            probeFailure = result;
        }

        var cli = this.cliSource is null
            ? null
            : await this.cliSource.TryFetchAsync(cancellationToken).ConfigureAwait(false);
        if (cli is UsageFetchResult.Success)
        {
            return cli;
        }

        if (probeFailure is not null)
        {
            return probeFailure;
        }

        if (cli is not null)
        {
            return cli;
        }

        if (appEndpoints.Count > 0)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                "Antigravity is running but its local token was not found. Restart Antigravity and try again."));
        }

        return Failure(this.cliSource is null
            ? ProviderError.NotConfigured("Antigravity isn't running. Launch Antigravity and refresh.")
            : ProviderError.NotConfigured(
                "Antigravity isn't running and the Antigravity CLI (`agy`) was not found. "
                + "Open Antigravity, or install `agy` and sign in once."));
    }

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);
}
