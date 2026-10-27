using CodexBar.Core.Identity;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Tests.Support;

internal static class TestSnapshots
{
    public static UsageSnapshot Codex(
        DateTimeOffset updatedAt,
        double session = 0d,
        double weekly = 0d,
        IReadOnlyList<NamedRateWindow>? extra = null,
        ProviderIdentity? identity = null) =>
        new(
            ProviderKeys.Codex,
            updatedAt,
            primary: new RateWindow(session, TimeSpan.FromHours(5), updatedAt.AddHours(3)),
            secondary: new RateWindow(weekly, TimeSpan.FromDays(7), updatedAt.AddDays(2)),
            extraWindows: extra,
            identity: identity ?? new ProviderIdentity(planName: "Plus"));

    public static UsageSnapshot Claude(
        DateTimeOffset updatedAt,
        double session = 0d,
        double weekly = 0d) =>
        new(
            ProviderKeys.Claude,
            updatedAt,
            primary: new RateWindow(session, TimeSpan.FromHours(5), updatedAt.AddHours(1)),
            secondary: new RateWindow(weekly, TimeSpan.FromDays(7), updatedAt.AddDays(3)),
            identity: new ProviderIdentity(planName: "Max"));

    public static IUsageProvider StubProvider(ProviderKey key, string displayName) =>
        new StubUsageProvider(new ProviderDescriptor(key, displayName));

    private sealed class StubUsageProvider(ProviderDescriptor descriptor) : IUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = descriptor;

        public Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UsageFetchResult>(
                new UsageFetchResult.Failure(ProviderError.NotConfigured("stub")));
    }
}
