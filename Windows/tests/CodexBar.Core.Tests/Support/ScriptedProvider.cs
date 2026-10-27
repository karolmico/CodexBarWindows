using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Tests.Support;

/// <summary>Provider whose results are scripted per call, for coordinator tests.</summary>
internal sealed class ScriptedProvider : IUsageProvider
{
    private readonly Queue<UsageFetchResult> results = new();
    private TaskCompletionSource? gate;

    public ScriptedProvider(ProviderKey key, string displayName)
    {
        this.Descriptor = new ProviderDescriptor(key, displayName);
    }

    public ProviderDescriptor Descriptor { get; }

    public int Calls { get; private set; }

    public void WillReturn(UsageFetchResult result) => this.results.Enqueue(result);

    public void BlockUntilReleased() => this.gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => this.gate?.SetResult();

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        this.Calls++;
        if (this.gate is { } currentGate)
        {
            await currentGate.Task.WaitAsync(cancellationToken);
        }

        return this.results.Count > 0
            ? this.results.Dequeue()
            : new UsageFetchResult.Failure(ProviderError.NotConfigured("no result scripted"));
    }

    public static UsageFetchResult Ok(ProviderKey key, DateTimeOffset updatedAt, double session = 10d, double weekly = 20d) =>
        new UsageFetchResult.Success(new UsageSnapshot(
            key,
            updatedAt,
            new RateWindow(session, TimeSpan.FromHours(5), updatedAt.AddHours(2)),
            new RateWindow(weekly, TimeSpan.FromDays(7), updatedAt.AddDays(3))));

    public static UsageFetchResult Error(ProviderErrorKind kind, string message) =>
        new UsageFetchResult.Failure(new ProviderError(kind, message));
}
