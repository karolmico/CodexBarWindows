using CodexBar.Core.Identity;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Usage;

/// <summary>Complete usage report for one provider at one instant. Immutable.</summary>
public sealed record UsageSnapshot
{
    public UsageSnapshot(
        ProviderKey provider,
        DateTimeOffset updatedAt,
        RateWindow? primary = null,
        RateWindow? secondary = null,
        IReadOnlyList<NamedRateWindow>? extraWindows = null,
        ProviderIdentity? identity = null)
    {
        this.Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.UpdatedAt = updatedAt;
        this.Primary = primary;
        this.Secondary = secondary;
        this.ExtraWindows = extraWindows ?? [];
        this.Identity = identity;
    }

    public ProviderKey Provider { get; }

    /// <summary>Session window (typically five hours), when the provider reports one.</summary>
    public RateWindow? Primary { get; }

    /// <summary>Weekly window, when the provider reports one.</summary>
    public RateWindow? Secondary { get; }

    public IReadOnlyList<NamedRateWindow> ExtraWindows { get; }

    public ProviderIdentity? Identity { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IEnumerable<RateWindow> MeasuredWindows =>
        new[] { this.Primary, this.Secondary }
            .OfType<RateWindow>()
            .Concat(this.ExtraWindows.Where(static window => window.UsageKnown).Select(static window => window.Window))
            .Where(static window => window.IsMeasured);

    public double? HighestUsedPercent =>
        this.MeasuredWindows.Select(static window => (double?)window.UsedPercent).Max();

    public bool IsExhausted => this.MeasuredWindows.Any(static window => window.IsExhausted);

    public bool HasMeasuredWindows => this.MeasuredWindows.Any();
}
