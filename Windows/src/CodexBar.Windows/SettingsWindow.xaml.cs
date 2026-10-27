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
    private readonly IReadOnlyList<int> extraThresholds;

    public SettingsWindow(AppSettings settings, IReadOnlyList<ProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(providers);

        this.InitializeComponent();

        this.rows = new ObservableCollection<ProviderRow>(BuildRows(settings, providers));
        this.ProviderList.ItemsSource = this.rows;
        this.extraThresholds = settings.NotificationThresholds
            .Where(static threshold => threshold is not 80 and not 95)
            .ToArray();
        this.RefreshFloorBox.Text =
            ((int)Math.Round(settings.RefreshFloor.TotalMinutes)).ToString(CultureInfo.InvariantCulture);
        this.Threshold80Box.IsChecked = settings.NotificationThresholds.Contains(80);
        this.Threshold95Box.IsChecked = settings.NotificationThresholds.Contains(95);
        this.ResetNotificationsBox.IsChecked = settings.NotifyResets;
        this.QuietStartBox.Text = settings.QuietHoursStart?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        this.QuietEndBox.Text = settings.QuietHoursEnd?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        this.StartWithWindowsBox.IsChecked = settings.StartWithWindows;
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

        if (!TryParseQuietHours(
            this.QuietStartBox.Text,
            this.QuietEndBox.Text,
            out var quietStart,
            out var quietEnd,
            out var quietError))
        {
            MessageBox.Show(
                this,
                quietError!,
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            this.QuietStartBox.Focus();
            return;
        }

        var thresholds = new List<int>(this.extraThresholds);
        if (this.Threshold80Box.IsChecked == true)
        {
            thresholds.Add(80);
        }

        if (this.Threshold95Box.IsChecked == true)
        {
            thresholds.Add(95);
        }

        this.Result = new AppSettings(
            this.rows.Select(row => new ProviderPreference(row.Key, row.Enabled)),
            TimeSpan.FromMinutes(minutes),
            notificationThresholds: thresholds,
            notifyResets: this.ResetNotificationsBox.IsChecked == true,
            quietHoursStart: quietStart,
            quietHoursEnd: quietEnd,
            startWithWindows: this.StartWithWindowsBox.IsChecked == true);

        this.DialogResult = true;
    }

    private static bool TryParseQuietHours(
        string startText,
        string endText,
        out TimeOnly? start,
        out TimeOnly? end,
        out string? error)
    {
        start = null;
        end = null;
        error = null;

        var hasStart = !string.IsNullOrWhiteSpace(startText);
        var hasEnd = !string.IsNullOrWhiteSpace(endText);
        if (!hasStart && !hasEnd)
        {
            return true;
        }

        if (!hasStart || !hasEnd)
        {
            error = "Quiet hours need both a start and an end, or leave both empty.";
            return false;
        }

        if (!TimeOnly.TryParseExact(
                startText.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedStart)
            || !TimeOnly.TryParseExact(
                endText.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedEnd))
        {
            error = "Quiet hours must use HH:mm, for example 22:00 and 07:00.";
            return false;
        }

        if (parsedStart == parsedEnd)
        {
            error = "Quiet hours need different start and end times.";
            return false;
        }

        start = parsedStart;
        end = parsedEnd;
        return true;
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
