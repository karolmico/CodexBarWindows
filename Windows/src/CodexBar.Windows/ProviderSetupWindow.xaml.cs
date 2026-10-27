using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexBar.Core.Credentials;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Setup;

namespace CodexBar.Windows;

/// <summary>
/// The place a user connects a provider. It shows the exact CLI command and credential file for the
/// selected provider, offers a saved personal access token where the app can use one, and runs a
/// single-provider connection check. Nothing here writes a provider's own credential file.
/// </summary>
public partial class ProviderSetupWindow : Window
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x70));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x4B));
    private static readonly Brush ProblemBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));

    private readonly Func<ProviderKey, ProviderState?> currentState;
    private readonly Func<ProviderKey, CancellationToken, Task> checkConnection;
    private readonly IProviderTokenStore tokenStore;
    private readonly Func<ProviderKey, bool>? isCheckable;
    private readonly Dictionary<ProviderKey, DateTimeOffset> checkedAt = [];
    private ProviderSetup? current;
    private bool isChecking;

    public ProviderSetupWindow(
        IReadOnlyList<ProviderSetup> setups,
        Func<ProviderKey, ProviderState?> currentState,
        Func<ProviderKey, CancellationToken, Task> checkConnection,
        IProviderTokenStore tokenStore,
        ProviderKey? initialProvider = null,
        Func<ProviderKey, bool>? isCheckable = null)
    {
        ArgumentNullException.ThrowIfNull(setups);
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.checkConnection = checkConnection ?? throw new ArgumentNullException(nameof(checkConnection));
        this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        this.isCheckable = isCheckable;

        this.InitializeComponent();
        this.ProviderList.ItemsSource = setups;

        var initial = setups.FirstOrDefault(setup => setup.Key == initialProvider)
            ?? setups.FirstOrDefault();
        if (initial is not null)
        {
            this.ProviderList.SelectedItem = initial;
        }
    }

    private void OnProviderSelected(object sender, SelectionChangedEventArgs e) =>
        this.ShowProvider(this.ProviderList.SelectedItem as ProviderSetup);

    private void ShowProvider(ProviderSetup? setup)
    {
        this.current = setup;
        this.TokenBox.Clear();
        if (setup is null)
        {
            return;
        }

        this.ProviderHeading.Text = setup.DisplayName;
        this.ProviderSummary.Text = setup.Summary;
        this.StepsList.ItemsSource = setup.Steps;

        this.CommandSection.Visibility = setup.Command is null ? Visibility.Collapsed : Visibility.Visible;
        this.CommandText.Text = setup.Command ?? string.Empty;

        this.PathSection.Visibility = setup.CredentialsPath is null ? Visibility.Collapsed : Visibility.Visible;
        this.PathText.Text = setup.CredentialsPath ?? string.Empty;
        this.PathNoteText.Text = setup.PathNote ?? string.Empty;
        this.PathNoteText.Visibility = setup.PathNote is null ? Visibility.Collapsed : Visibility.Visible;

        this.TokenSection.Visibility =
            setup.SupportsPersonalAccessToken ? Visibility.Visible : Visibility.Collapsed;
        this.TokenLabelText.Text = setup.TokenLabel ?? "Personal access token";
        this.TokenHintText.Text = setup.TokenHint ?? string.Empty;
        this.TokenHintText.Visibility = setup.TokenHint is null ? Visibility.Collapsed : Visibility.Visible;
        this.UpdateTokenState(setup);

        this.OpenHelpButton.Visibility = setup.HelpUrl is null ? Visibility.Collapsed : Visibility.Visible;
        this.OpenHelpButton.Content = setup.HelpLabel ?? "Open help page";

        this.SetStatus(ProviderSetupStatusText.Describe(this.currentState(setup.Key), DateTimeOffset.Now));
        this.CheckedAtText.Text = this.checkedAt.TryGetValue(setup.Key, out var checkedAt)
            ? $"Checked at {checkedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}."
            : string.Empty;
    }

    private async void OnCheckConnection(object sender, RoutedEventArgs e)
    {
        if (this.current is { } setup)
        {
            await this.CheckAsync(setup);
        }
    }

    private async Task CheckAsync(ProviderSetup setup)
    {
        if (this.isChecking)
        {
            return;
        }

        if (this.isCheckable is { } isCheckable && !isCheckable(setup.Key))
        {
            this.SetProblem("This provider is turned off in Settings. Enable it there, then check again.");
            return;
        }

        this.isChecking = true;
        this.SetBusy(true);
        this.StatusText.Foreground = MutedBrush;
        this.StatusText.Text = $"Checking {setup.DisplayName}…";
        this.CheckedAtText.Text = string.Empty;

        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            await this.checkConnection(setup.Key, timeout.Token);

            var now = DateTimeOffset.Now;
            this.checkedAt[setup.Key] = now;
            if (this.current?.Key != setup.Key)
            {
                return;
            }

            this.SetStatus(ProviderSetupStatusText.Describe(this.currentState(setup.Key), now));
            this.CheckedAtText.Text = $"Checked at {now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}.";
        }
        catch (OperationCanceledException)
        {
            this.SetProblem($"The check timed out after {CheckTimeout.TotalSeconds:0} seconds. Try again.");
        }
        catch (Exception exception)
        {
            // UI boundary: an unexpected fault in the check must not take the tray down.
            this.SetProblem($"The check failed: {exception.Message}");
        }
        finally
        {
            this.isChecking = false;
            this.SetBusy(false);
        }
    }

    private async void OnSaveToken(object sender, RoutedEventArgs e)
    {
        if (this.current is not { SupportsPersonalAccessToken: true } setup)
        {
            return;
        }

        var token = this.TokenBox.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(
                this,
                "Paste a token before saving.",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            this.TokenBox.Focus();
            return;
        }

        try
        {
            this.tokenStore.SaveToken(setup.Key, token);
        }
        catch (Exception exception) when (exception is CredentialStoreException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        this.TokenBox.Clear();
        this.UpdateTokenState(setup);
        await this.CheckAsync(setup);
    }

    private async void OnClearToken(object sender, RoutedEventArgs e)
    {
        if (this.current is not { SupportsPersonalAccessToken: true } setup)
        {
            return;
        }

        try
        {
            this.tokenStore.ClearToken(setup.Key);
        }
        catch (Exception exception) when (exception is CredentialStoreException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        this.UpdateTokenState(setup);
        await this.CheckAsync(setup);
    }

    private void OnRunCommand(object sender, RoutedEventArgs e)
    {
        if (this.current?.Command is not { } command)
        {
            return;
        }

        try
        {
            // The command comes from ProviderSetupCatalog, never from user input. `cmd /k` keeps the
            // window open after an interactive sign-in finishes so the result stays readable.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k {command}") { UseShellExecute = true });
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(
                this,
                $"A terminal could not be opened: {exception.Message}\n\nRun this command yourself:\n{command}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCopyCommand(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(this.CommandText.Text);
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                this,
                "The clipboard is busy. Select the command and press Ctrl+C.",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var path = this.current?.ExpandedCredentialsPath;
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                return;
            }

            MessageBox.Show(
                this,
                "That folder does not exist yet. Run the sign-in command first.",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"The folder could not be opened: {exception.Message}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnOpenHelp(object sender, RoutedEventArgs e)
    {
        if (this.current?.HelpUrl is not { } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            MessageBox.Show(
                this,
                $"The page could not be opened. Its address is {uri.AbsoluteUri}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => this.Close();

    private void UpdateTokenState(ProviderSetup setup)
    {
        var saved = this.tokenStore.HasToken(setup.Key);
        this.TokenStateText.Text = saved
            ? "A token is saved for this Windows account. It is used before the GitHub CLI."
            : "No token saved here yet. CodexBar will use the GitHub CLI or GITHUB_TOKEN.";
        this.ClearTokenButton.IsEnabled = saved;
    }

    private void SetBusy(bool busy)
    {
        this.CheckButton.IsEnabled = !busy;
        this.CheckButton.Content = busy ? "Checking…" : "Check connection";
        this.SaveTokenButton.IsEnabled = !busy;
        this.ClearTokenButton.IsEnabled = !busy && this.current is { } setup && this.tokenStore.HasToken(setup.Key);
    }

    private void SetStatus(ProviderSetupStatus status)
    {
        this.StatusText.Text = status.Text;
        this.StatusText.Foreground = status.IsProblem
            ? ProblemBrush
            : status.IsConnected
                ? SuccessBrush
                : MutedBrush;
    }

    private void SetProblem(string message)
    {
        this.StatusText.Text = message;
        this.StatusText.Foreground = ProblemBrush;
    }
}
