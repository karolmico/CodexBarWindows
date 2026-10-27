using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using CodexBar.Core.Providers;
using CodexBar.Core.Settings;

namespace CodexBar.Windows;

/// <summary>
/// Minimal settings surface: provider enablement and order plus the refresh floor. Persistence lives in
/// <see cref="SettingsStore"/>; this window only edits a copy and returns it on Save.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<ProviderRow> rows;

    public SettingsWindow(AppSettings settings, IReadOnlyList<ProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(providers);

        this.InitializeComponent();

        this.rows = new ObservableCollection<ProviderRow>(BuildRows(settings, providers));
        this.ProviderList.ItemsSource = this.rows;
        this.RefreshFloorBox.Text =
            ((int)Math.Round(settings.RefreshFloor.TotalMinutes)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Settings produced by Save; null when the user cancelled.</summary>
    public AppSettings? Result { get; private set; }

    private static IEnumerable<ProviderRow> BuildRows(
        AppSettings settings,
        IReadOnlyList<ProviderDescriptor> providers)
    {
        var names = providers.ToDictionary(provider => provider.Key, provider => provider.DisplayName);
        var configured = settings.Providers.Select(preference => new ProviderRow(
            names.TryGetValue(preference.Key, out var name) ? name : preference.Key.Value,
            preference.Key,
            preference.Enabled));

        var missing = providers
            .Where(provider => settings.Providers.All(preference => preference.Key != provider.Key))
            .Select(provider => new ProviderRow(provider.DisplayName, provider.Key, provider.EnabledByDefault));

        return configured.Concat(missing);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => this.Move(sender, offset: -1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => this.Move(sender, offset: 1);

    private void Move(object sender, int offset)
    {
        if (sender is not FrameworkElement { Tag: ProviderRow row })
        {
            return;
        }

        var index = this.rows.IndexOf(row);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= this.rows.Count)
        {
            return;
        }

        this.rows.Move(index, target);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(this.RefreshFloorBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || minutes < AppSettings.MinRefreshFloorMinutes
            || minutes > AppSettings.MaxRefreshFloorMinutes)
        {
            MessageBox.Show(
                this,
                $"Enter a whole number of minutes between {AppSettings.MinRefreshFloorMinutes} and {AppSettings.MaxRefreshFloorMinutes}.",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            this.RefreshFloorBox.Focus();
            return;
        }

        this.Result = new AppSettings(
            this.rows.Select(row => new ProviderPreference(row.Key, row.Enabled)),
            TimeSpan.FromMinutes(minutes));

        this.DialogResult = true;
    }

    public sealed class ProviderRow : INotifyPropertyChanged
    {
        private bool enabled;

        public ProviderRow(string name, ProviderKey key, bool enabled)
        {
            this.Name = name;
            this.Key = key;
            this.enabled = enabled;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }

        public ProviderKey Key { get; }

        public bool Enabled
        {
            get => this.enabled;
            set
            {
                if (this.enabled == value)
                {
                    return;
                }

                this.enabled = value;
                this.OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
