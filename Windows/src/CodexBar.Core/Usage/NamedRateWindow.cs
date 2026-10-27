namespace CodexBar.Core.Usage;

/// <summary>A rate window with a stable identity, used for extra or model-specific lanes.</summary>
public sealed record NamedRateWindow
{
    public NamedRateWindow(string id, string title, RateWindow window, bool usageKnown = true)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Named window id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Named window title is required.", nameof(title));
        }

        this.Id = id.Trim();
        this.Title = title.Trim();
        this.Window = window ?? throw new ArgumentNullException(nameof(window));
        this.UsageKnown = usageKnown;
    }

    public string Id { get; }

    public string Title { get; }

    public RateWindow Window { get; }

    /// <summary>False when the provider exposes reset metadata without a usage value.</summary>
    public bool UsageKnown { get; }
}
