namespace CodexBar.Core.Providers;

/// <summary>Resolves providers in the user's configured order.</summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<ProviderKey, IUsageProvider> providersByKey;

    public ProviderRegistry(IEnumerable<IUsageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var ordered = new List<IUsageProvider>();
        this.providersByKey = [];
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (!this.providersByKey.TryAdd(provider.Descriptor.Key, provider))
            {
                throw new ArgumentException(
                    $"Duplicate provider registration for '{provider.Descriptor.Key}'.",
                    nameof(providers));
            }

            ordered.Add(provider);
        }

        this.All = ordered;
    }

    /// <summary>Every registered provider, in registration order.</summary>
    public IReadOnlyList<IUsageProvider> All { get; }

    /// <summary>
    /// Providers in the configured order. Unknown keys are skipped and duplicates keep their first
    /// position, so a stale settings file can never hide or duplicate a provider.
    /// </summary>
    public IReadOnlyList<IUsageProvider> Resolve(IEnumerable<ProviderKey> configuredOrder)
    {
        ArgumentNullException.ThrowIfNull(configuredOrder);

        var resolved = new List<IUsageProvider>();
        var seen = new HashSet<ProviderKey>();
        foreach (var key in configuredOrder)
        {
            if (key is null || !seen.Add(key))
            {
                continue;
            }

            if (this.providersByKey.TryGetValue(key, out var provider))
            {
                resolved.Add(provider);
            }
        }

        return resolved;
    }

    public bool TryGet(ProviderKey key, out IUsageProvider provider) =>
        this.providersByKey.TryGetValue(key, out provider!);
}
