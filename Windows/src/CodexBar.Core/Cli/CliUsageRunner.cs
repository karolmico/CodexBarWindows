using System.Diagnostics;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Cli;

/// <summary>
/// Fetches every selected provider once and converts both outcomes and unexpected exceptions into
/// <see cref="CliProviderResult"/> values. The CLI never dies because one provider threw.
/// </summary>
public sealed class CliUsageRunner
{
    public async Task<IReadOnlyList<CliProviderResult>> RunAsync(
        IEnumerable<IUsageProvider> providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var tasks = providers.Select(provider => this.FetchAsync(provider, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<CliProviderResult> FetchAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var started = Stopwatch.GetTimestamp();
        UsageFetchResult result;
        try
        {
            result = await provider.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Same containment rule as the tray: an unexpected throw is an Unknown error value, not a crash.
            result = new UsageFetchResult.Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                $"{provider.Descriptor.DisplayName} failed unexpectedly ({exception.GetType().Name}).",
                cause: exception));
        }

        var duration = Stopwatch.GetElapsedTime(started);
        var descriptor = provider.Descriptor;
        var source = CliSources.For(descriptor.Key);

        return result switch
        {
            UsageFetchResult.Success success => new CliProviderResult(
                descriptor.Key,
                descriptor.DisplayName,
                source,
                success.Snapshot,
                Error: null,
                duration),
            UsageFetchResult.Failure failure => new CliProviderResult(
                descriptor.Key,
                descriptor.DisplayName,
                source,
                Snapshot: null,
                failure.Error,
                duration),
            _ => throw new InvalidOperationException($"Unhandled fetch result '{result.GetType().Name}'."),
        };
    }
}

/// <summary>Help text shared by the console host and its tests.</summary>
public static class CliHelp
{
    public static string Usage =>
        """
        CodexBar - ambient AI subscription usage

        Usage:
          codexbar usage [options]

        Options:
          -f, --format <text|json>   Output format (default: text)
              --json                 Shortcut for --format json
              --pretty               Indent JSON output
          -p, --provider <id>        Only this provider; repeatable or comma-separated
              --require-all          Treat any failed provider as a total failure
          -h, --help                 Show this help
          -V, --version              Show the version

        Providers: codex, claude, copilot, cursor, antigravity, opencode
        Exit codes: 0 success, 1 partial failure, 2 total failure, 64 usage error
        """;

    public static string Version(string version) => $"codexbar {version}";
}
