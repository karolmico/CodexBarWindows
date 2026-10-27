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
        bool isSyntheticPlaceholder = false,
        bool isDisabled = false,
        string? disabledReason = null,
        string? group = null)
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
        this.IsDisabled = isDisabled;
        this.DisabledReason = string.IsNullOrWhiteSpace(disabledReason) ? null : disabledReason.Trim();
        this.Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
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

    /// <summary>
    /// The provider reports this lane but has switched it off, e.g. Antigravity's five-hour limit while
    /// the weekly limit is exhausted. It carries no usage value and must never move a threshold, but it
    /// is still worth showing so a disappearing row does not look like a bug.
    /// </summary>
    public bool IsDisabled { get; }

    /// <summary>The provider's own explanation for a disabled lane, when it supplies one.</summary>
    public string? DisabledReason { get; }

    /// <summary>
    /// The model family the lane belongs to (e.g. Antigravity's "Gemini Models"). Null for providers
    /// that do not group their lanes; the flyout renders a header only when a group is present.
    /// </summary>
    public string? Group { get; }

    public bool IsMeasured => !this.IsSyntheticPlaceholder && !this.IsDisabled;

    public bool IsExhausted => this.IsMeasured && this.UsedPercent >= 100d;

    public double RemainingPercent => Math.Clamp(100d - this.UsedPercent, 0d, 100d);

    public double DisplayUsedPercent => Math.Clamp(this.UsedPercent, 0d, 100d);
}
