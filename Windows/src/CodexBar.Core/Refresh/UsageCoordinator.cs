using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Refresh;

/// <summary>
/// Owns the current usage state for a fixed set of providers: runs fetches, keeps the last good snapshot
/// when a refresh fails, counts consecutive failures for backoff, and coalesces concurrent refreshes.
/// </summary>
public sealed class UsageCoordinator
{
    private readonly object gate = new();
    private readonly IUsageProvider[] providers;
    private readonly Dictionary<ProviderKey, Entry> entries;
    private readonly RefreshPolicy policy;
    private readonly Func<DateTimeOffset> clock;
    private Task? inFlight;

    public UsageCoordinator(
        IEnumerable<IUsageProvider> providers,
        RefreshPolicy? policy = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
        this.policy = policy ?? new RefreshPolicy();
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.entries = this.providers.ToDictionary(
            provider => provider.Descriptor.Key,
            provider => new Entry(provider));
    }

    public event EventHandler? Changed;

    public bool IsRefreshing { get; private set; }

    public IReadOnlyList<ProviderState> States =>
        this.providers
            .Select(provider => this.entries[provider.Descriptor.Key])
            .Select(entry => new ProviderState(
                entry.Provider.Descriptor.Key,
                entry.Provider.Descriptor.DisplayName,
                entry.Snapshot,
                entry.Error))
            .ToArray();

    /// <summary>Refreshes every provider, reusing an in-flight refresh rather than starting a second one.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            if (this.inFlight is not null)
            {
                return this.inFlight;
            }

            var task = this.RefreshCoreAsync(cancellationToken);
            // A synchronously completed refresh leaves no in-flight work to share; caching it would
            // make every later refresh a no-op.
            if (!task.IsCompleted)
            {
                this.inFlight = task;
            }

            return task;
        }
    }

    /// <summary>Seeds the last good snapshots loaded from the cache; a failed first refresh keeps showing them.</summary>
    public void ApplyCached(IEnumerable<UsageSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var applied = false;
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || !this.entries.TryGetValue(snapshot.Provider, out var entry))
            {
                continue;
            }

            entry.Snapshot = snapshot;
            entry.Error = null;
            entry.Failures = 0;
            applied = true;
        }

        if (applied)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Snapshots currently worth persisting; providers without data are omitted.</summary>
    public IReadOnlyList<UsageSnapshot> Snapshots =>
        this.providers
            .Select(provider => this.entries[provider.Descriptor.Key].Snapshot)
            .OfType<UsageSnapshot>()
            .ToArray();

    /// <summary>Shortest wait any provider needs before its next fetch.</summary>
    public TimeSpan NextDelay()
    {
        if (this.providers.Length == 0)
        {
            return this.policy.IdleInterval;
        }

        var now = this.clock();
        return this.providers
            .Select(provider => this.entries[provider.Descriptor.Key])
            .Min(entry => this.policy.NextDelay(new RefreshState(now, entry.Snapshot, entry.Error, entry.Failures)));
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        this.IsRefreshing = true;
        this.Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            await Task.WhenAll(this.providers.Select(provider => this.FetchIntoAsync(provider, cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            lock (this.gate)
            {
                this.IsRefreshing = false;
                this.inFlight = null;
            }

            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task FetchIntoAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        var entry = this.entries[provider.Descriptor.Key];
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
            result = new UsageFetchResult.Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                $"{provider.Descriptor.DisplayName} failed unexpectedly.",
                cause: exception));
        }

        lock (this.gate)
        {
            switch (result)
            {
                case UsageFetchResult.Success success:
                    entry.Snapshot = success.Snapshot;
                    entry.Error = null;
                    entry.Failures = 0;
                    break;
                case UsageFetchResult.Failure failure:
                    entry.Error = failure.Error;
                    entry.Failures++;
                    break;
            }
        }
    }

    private sealed class Entry
    {
        public Entry(IUsageProvider provider) => this.Provider = provider;

        public IUsageProvider Provider { get; }

        public Usage.UsageSnapshot? Snapshot { get; set; }

        public ProviderError? Error { get; set; }

        public int Failures { get; set; }
    }
}
