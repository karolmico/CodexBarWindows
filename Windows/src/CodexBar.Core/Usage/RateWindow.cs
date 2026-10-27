namespace CodexBar.Core.Usage;

/// <summary>
/// One quota lane reported by a provider. The provider value is kept untouched so pace
/// diagnostics can see over-quota usage; only display projections clamp.
/// </summary>
public sealed record RateWindow
{
    public RateWindow(
        double usedPercent,
        TimeSpan? window = null,
        DateTimeOffset? resetsAt = null,
        string? resetDescription = null,
        string? title = null,
        double? quotaLimit = null,
        string? quotaUnit = null,
        bool isSyntheticPlaceholder = false)
    {
        if (!double.IsFinite(usedPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(usedPercent), usedPercent, "Usage percentage must be finite.");
        }

        this.UsedPercent = usedPercent;
        this.Window = window;
        this.ResetsAt = resetsAt;
        this.ResetDescription = resetDescription;
        this.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        this.QuotaLimit = quotaLimit;
        this.QuotaUnit = quotaUnit;
        this.IsSyntheticPlaceholder = isSyntheticPlaceholder;
    }

    public double UsedPercent { get; }

    public TimeSpan? Window { get; }

    public DateTimeOffset? ResetsAt { get; }

    public string? ResetDescription { get; }

    public string? Title { get; }

    public double? QuotaLimit { get; }

    public string? QuotaUnit { get; }

    /// <summary>A window invented for layout stability; it carries no measured value.</summary>
    public bool IsSyntheticPlaceholder { get; }

    public bool IsMeasured => !this.IsSyntheticPlaceholder;

    public bool IsExhausted => this.IsMeasured && this.UsedPercent >= 100d;

    public double RemainingPercent => Math.Clamp(100d - this.UsedPercent, 0d, 100d);

    public double DisplayUsedPercent => Math.Clamp(this.UsedPercent, 0d, 100d);
}
