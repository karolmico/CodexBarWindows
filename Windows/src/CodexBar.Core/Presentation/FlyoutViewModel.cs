using System.ComponentModel;

namespace CodexBar.Core.Presentation;

/// <summary>
/// The flyout's UI state: which provider tab is open plus the latest projection. Selection lives here
/// because the window is rebuilt from a fresh <see cref="FlyoutModel"/> on every refresh, and a
/// selected tab must survive that rebuild without the composition root tracking it.
/// </summary>
public sealed class FlyoutViewModel : INotifyPropertyChanged
{
    private FlyoutModel model;
    private string? selectedKey;

    public FlyoutViewModel(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.model = model;
        this.selectedKey = PreferredKey(model) ?? model.Providers.FirstOrDefault()?.Key;
        this.Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Tabs, with <see cref="FlyoutProviderModel.IsSelected"/> set on exactly one entry.</summary>
    public IReadOnlyList<FlyoutProviderModel> Providers { get; private set; } = [];

    /// <summary>Detail pane for the selected tab; null only when no provider is configured.</summary>
    public FlyoutProviderModel? Selected { get; private set; }

    public int ProviderCount => this.Providers.Count;

    public bool HasProviders => this.Providers.Count > 0;

    public string FooterText => this.model.FooterText;

    public bool IsRefreshing => this.model.IsRefreshing;

    public bool HasData => this.model.HasData;

    public string? SelectedKey => this.selectedKey;

    /// <summary>Switches the detail pane. Returns false for a key that is not on screen.</summary>
    public bool Select(string? key)
    {
        if (key is null
            || !this.model.Providers.Any(provider => string.Equals(provider.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (string.Equals(this.selectedKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        this.selectedKey = key;
        this.Refresh();
        return true;
    }

    /// <summary>Moves the selection by <paramref name="delta"/> tabs, wrapping at both ends.</summary>
    public bool SelectAdjacent(int delta)
    {
        var count = this.Providers.Count;
        if (count == 0)
        {
            return false;
        }

        var index = -1;
        for (var candidate = 0; candidate < count; candidate++)
        {
            if (this.Providers[candidate].IsSelected)
            {
                index = candidate;
                break;
            }
        }

        var next = ((index < 0 ? 0 : index) + delta % count + count) % count;
        return this.Select(this.Providers[next].Key);
    }

    /// <summary>
    /// Takes a new projection and keeps the current tab. A provider that disappeared (disabled in
    /// settings) hands the selection to a trimmed-down earlier choice instead of blanking the pane.
    /// </summary>
    public void Apply(FlyoutModel next)
    {
        ArgumentNullException.ThrowIfNull(next);

        this.model = next;
        if (PreferredKey(next) is { } preferred)
        {
            this.selectedKey = preferred;
        }
        else if (this.selectedKey is null
            || !next.Providers.Any(provider => string.Equals(provider.Key, this.selectedKey, StringComparison.OrdinalIgnoreCase)))
        {
            this.selectedKey = next.Providers.FirstOrDefault()?.Key;
        }

        this.Refresh();
    }

    private static string? PreferredKey(FlyoutModel model) =>
        model.Providers.FirstOrDefault(static provider => provider.IsHighlighted)?.Key;

    private void Refresh()
    {
        this.Providers = this.model.Providers
            .Select(provider => provider with
            {
                IsSelected = string.Equals(provider.Key, this.selectedKey, StringComparison.OrdinalIgnoreCase),
            })
            .ToArray();
        this.Selected = this.Providers.FirstOrDefault(static provider => provider.IsSelected);
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName: null));
    }
}
