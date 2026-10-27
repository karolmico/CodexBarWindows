using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CodexBar.Core.Credentials;
using CodexBar.Core.Diagnostics;
using CodexBar.Core.Notifications;
using CodexBar.Core.Persistence;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Refresh;
using CodexBar.Core.Settings;
using CodexBar.Core.Setup;
using CodexBar.Providers.Windows;
using CodexBar.Windows.Diagnostics;

namespace CodexBar.Windows;

public partial class App : Application
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer refreshTimer = new(DispatcherPriority.Background);
    private readonly CancellationTokenSource shutdown = new();
    private ProviderRegistry registry = null!;
    private SettingsStore settingsStore = null!;
    private SnapshotCache snapshotCache = null!;
    private FileProviderTokenStore tokenStore = null!;
    private AppSettings settings = null!;
    private UsageCoordinator? coordinator;
    private Tray.TrayHost? trayHost;
    private FlyoutWindow? flyout;
    private SingleInstance? singleInstance;
    private NotificationDecider notificationDecider = new();
    private RollingLogStore? logStore;
    private DiagnosticsService? diagnostics;
    private bool isAutostartLaunch;
    private bool hasReportedUnhandledError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;
        this.isAutostartLaunch = e.Args.Any(static argument =>
            string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CodexBar");

            // A second launch hands the activation signal to the running instance and exits before
            // creating any tray state.
            this.singleInstance = SingleInstance.Acquire(
                () => this.Dispatcher.BeginInvoke(new Action(() => this.ShowFlyout())));
            if (!this.singleInstance.IsOwner)
            {
                this.singleInstance.Dispose();
                this.singleInstance = null;
                this.Shutdown();
                return;
            }

            this.logStore = new RollingLogStore(Path.Combine(dataDirectory, "logs"));
            this.diagnostics = new DiagnosticsService(dataDirectory);
            this.logStore.Info($"Starting CodexBar {AppVersion()} (autostart={this.isAutostartLaunch})");

            // Provider catalog. New providers are added in one place and shared with the CLI. Saved
            // token overrides live in one encrypted file under the CodexBar data folder.
            this.tokenStore = new FileProviderTokenStore(
                Path.Combine(dataDirectory, "provider-credentials.dat"),
                new DpapiDataProtector());
            this.registry = ProviderCatalog.Create(this.httpClient, this.tokenStore);

            this.settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
            this.snapshotCache = new SnapshotCache(Path.Combine(dataDirectory, "snapshots.json"));

            var loaded = this.settingsStore.Load(
                AppSettings.DefaultFor(this.registry.All.Select(provider => provider.Descriptor)));
            this.settings = loaded.Settings;
            AutostartRegistration.Apply(this.settings.StartWithWindows);

            this.coordinator = this.CreateCoordinator(this.settings);
            this.coordinator.ApplyCached(this.snapshotCache.Load());

            this.refreshTimer.Tick += this.OnRefreshTimerTick;

            this.trayHost = new Tray.TrayHost(this.BuildMenu);
            this.trayHost.ExitRequested += (_, _) => this.Shutdown();
            this.trayHost.RefreshRequested += (_, _) => _ = this.RefreshAsync();
            this.trayHost.SettingsRequested += (_, _) => this.ShowSettings();
            this.trayHost.ShowFlyoutRequested += (_, providerKey) => this.ShowFlyout(providerKey);
            this.trayHost.Start();

            if (loaded.Warning is { } warning)
            {
                this.logStore?.Warn($"Settings warning: {warning}");
                this.trayHost.Notify("CodexBar settings", warning, isWarning: true);
            }

            // An autostart launch stays invisible and gives Windows a few seconds to finish signing in.
            if (this.isAutostartLaunch)
            {
                this.refreshTimer.Interval = TimeSpan.FromSeconds(4);
                this.refreshTimer.Start();
            }
            else
            {
                _ = this.RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            // A tray app that dies here would disappear without a trace; surface the fault once.
            MessageBox.Show(
                $"CodexBar could not start its tray icon.\n\n{exception.Message}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            this.Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        this.DispatcherUnhandledException -= this.OnDispatcherUnhandledException;
        this.refreshTimer.Stop();
        this.shutdown.Cancel();
        this.shutdown.Dispose();
        this.flyout?.Close();
        this.flyout = null;
        this.trayHost?.Dispose();
        this.trayHost = null;
        this.singleInstance?.Dispose();
        this.singleInstance = null;
        this.logStore?.Info("Exiting.");
        this.httpClient.Dispose();
        base.OnExit(e);
    }

    private UsageCoordinator CreateCoordinator(AppSettings currentSettings)
    {
        var coordinator = new UsageCoordinator(
            this.registry.Resolve(currentSettings.EnabledProviderKeys),
            new RefreshPolicy(),
            () => DateTimeOffset.Now);

        coordinator.Changed += this.OnUsageChanged;
        return coordinator;
    }

    private TrayMenuModel BuildMenu() =>
        this.coordinator is null
            ? TrayMenuBuilder.Build([], DateTimeOffset.Now, isRefreshing: false)
            : TrayMenuBuilder.Build(
                this.coordinator.States,
                DateTimeOffset.Now,
                this.coordinator.IsRefreshing,
                settingsAvailable: true);

    private void Render()
    {
        this.trayHost?.Render(this.BuildMenu());
        if (this.flyout is { IsVisible: true } open && this.coordinator is not null)
        {
            open.Update(FlyoutBuilder.Build(
                this.coordinator.States,
                DateTimeOffset.Now,
                this.coordinator.IsRefreshing));
        }
    }

    private void ShowFlyout(string? providerKey = null)
    {
        if (this.coordinator is null)
        {
            return;
        }

        var previous = this.flyout;
        this.flyout = null;
        if (previous is not null)
        {
            try
            {
                previous.Close();
            }
            catch (InvalidOperationException)
            {
                // Already tearing down; its Closed handler clears the field.
            }
        }

        var window = new FlyoutWindow(FlyoutBuilder.Build(
            this.coordinator.States,
            DateTimeOffset.Now,
            this.coordinator.IsRefreshing,
            providerKey));
        window.RefreshRequested += (_, _) => _ = this.RefreshAsync();
        window.SettingsRequested += (_, _) => this.ShowSettings();
        window.AboutRequested += (_, _) => this.ShowAbout();
        window.ExitRequested += (_, _) => this.Shutdown();
        window.DiagnosticsRequested += (_, _) => this.CopyDiagnostics(window);
        window.ProviderSetupRequested += (_, key) => this.ShowProviderSetup(key);
        window.LinkRequested += (_, url) => OpenLink(url, this.logStore);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(this.flyout, window))
            {
                this.flyout = null;
            }
        };

        window.ShowAtNotificationArea();
        this.flyout = window;
    }

    private void OnUsageChanged(object? sender, EventArgs e) =>
        this.Dispatcher.BeginInvoke(this.Render, DispatcherPriority.Background);

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _ = this.RefreshAsync();

    private async Task RefreshAsync()
    {
        this.refreshTimer.Stop();
        if (this.coordinator is null)
        {
            return;
        }

        try
        {
            this.Render();
            await this.coordinator.RefreshAsync(this.shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        this.Render();
        this.LogRefreshOutcomes();
        this.PersistSnapshots();
        this.RaiseNotifications();

        var delay = this.coordinator.NextDelay();
        if (delay < this.settings.RefreshFloor)
        {
            delay = this.settings.RefreshFloor;
        }

        this.refreshTimer.Interval = delay;
        this.refreshTimer.Start();
    }

    private void PersistSnapshots()
    {
        if (this.coordinator is null)
        {
            return;
        }

        try
        {
            this.snapshotCache.Save(this.coordinator.Snapshots);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the cache only costs a slower first paint on the next launch.
        }
    }

    /// <summary>Writes one redacted line per provider so a later report has a real timeline.</summary>
    private void LogRefreshOutcomes()
    {
        if (this.coordinator is null)
        {
            return;
        }

        foreach (var state in this.coordinator.States)
        {
            this.LogProviderOutcome(state);
        }
    }

    private void LogProviderOutcome(ProviderState state)
    {
        if (this.coordinator is null || this.logStore is null)
        {
            return;
        }

        var duration = this.coordinator.Timings
            .FirstOrDefault(timing => timing.Key == state.Key)?.LastFetchDuration is { } elapsed
                ? string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalMilliseconds:0} ms")
                : "unknown";

        if (state.Error is { } error)
        {
            this.logStore.Warn(
                $"{state.Key.Value} failed ({error.Kind}) in {duration}: {error.Message}",
                error.Cause);
        }
        else if (state.Snapshot is { } snapshot)
        {
            this.logStore.Info(
                $"{state.Key.Value} ok in {duration}; {snapshot.MeasuredWindows.Count()} windows");
        }
        else
        {
            this.logStore.Info($"{state.Key.Value} returned no data in {duration}");
        }
    }

    private DiagnosticsCopyResult CopyDiagnosticsCore()
    {
        if (this.coordinator is null || this.settings is null || this.diagnostics is null)
        {
            return new DiagnosticsCopyResult(false, "No usage data yet.", FilePath: null);
        }

        var result = this.diagnostics.CaptureAndCopy(
            this.settings,
            this.coordinator.States,
            this.coordinator.Timings,
            DateTimeOffset.Now);

        this.logStore?.Info($"Diagnostics copy: copied={result.Copied}, file={result.FilePath is not null}");
        return result;
    }

    private void CopyDiagnostics(FlyoutWindow window)
    {
        var result = this.CopyDiagnosticsCore();
        window.ShowDiagnosticsStatus(result.Message, isError: !result.Copied);
    }

    private string CopyDiagnosticsFromSettings() => this.CopyDiagnosticsCore().Message;

    private static string AppVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Turns fresh provider state into toasts. The decider keeps arming state, so a refresh that did not
    /// cross a threshold stays silent; the cap keeps one noisy refresh from flooding the notification area.
    /// </summary>
    private void RaiseNotifications()
    {
        if (this.coordinator is null || this.trayHost is null || this.settings is null)
        {
            return;
        }

        var notifications = this.notificationDecider.Evaluate(
            this.coordinator.States,
            this.settings,
            DateTimeOffset.Now);

        foreach (var notification in notifications.Take(3))
        {
            this.trayHost.Notify(
                notification.Title,
                notification.Message,
                notification.IsWarning,
                notification.Provider.Value);
        }
    }

    private void ShowSettings()
    {
        var window = new SettingsWindow(
            this.settings,
            this.registry.All.Select(provider => provider.Descriptor).ToArray(),
            this.CopyDiagnosticsFromSettings)
        {
            Owner = null,
        };
        window.ProviderSetupRequested += (_, _) => this.ShowProviderSetup(owner: window);

        if (window.ShowDialog() != true || window.Result is not { } updated)
        {
            return;
        }

        try
        {
            this.settingsStore.Save(updated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Settings could not be saved.\n\n{exception.Message}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        this.settings = updated;
        this.notificationDecider = new NotificationDecider();
        AutostartRegistration.Apply(updated.StartWithWindows);
        var cached = this.coordinator?.Snapshots ?? [];
        if (this.coordinator is not null)
        {
            this.coordinator.Changed -= this.OnUsageChanged;
        }

        this.coordinator = this.CreateCoordinator(updated);
        this.coordinator.ApplyCached(cached);
        this.Render();
        _ = this.RefreshAsync();
    }

    /// <summary>Version, identity, and where to report a problem; reachable from the flyout menu.</summary>
    private void ShowAbout() =>
        MessageBox.Show(
            $"CodexBar {AppVersion()}\n\n"
            + "Ambient AI subscription usage in the notification area.\n"
            + "https://github.com/karolmico/CodexBarWindows",
            "About CodexBar",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    /// <summary>Opens a provider page in the default browser; a failed launch stays in the log.</summary>
    private static void OpenLink(string url, RollingLogStore? logStore)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            logStore?.Warn($"Refused to open a non-web link: {url}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logStore?.Warn($"Could not open {uri.Host}: {exception.Message}");
        }
    }

    /// <summary>
    /// Opens the provider setup dialog. It is reachable from the tray Settings window and from a
    /// failed provider card in the flyout, so "not configured" always has a next step.
    /// </summary>
    private void ShowProviderSetup(string? providerKey = null, Window? owner = null)
    {
        if (this.coordinator is null)
        {
            return;
        }

        var initial = ProviderKey.TryParse(providerKey, out var parsed) ? parsed : (ProviderKey?)null;
        var window = new ProviderSetupWindow(
            ProviderSetupCatalog.For(this.registry.All.Select(provider => provider.Descriptor)),
            key => this.coordinator?.States.FirstOrDefault(state => state.Key == key),
            this.CheckProviderConnectionAsync,
            this.tokenStore,
            initial,
            key => this.settings.EnabledProviderKeys.Contains(key))
        {
            Owner = owner,
        };

        window.ShowDialog();

        // The single-provider check already updated the coordinator; repaint and keep the new data
        // for the next launch.
        this.Render();
        this.PersistSnapshots();
    }

    private async Task CheckProviderConnectionAsync(ProviderKey key, CancellationToken cancellationToken)
    {
        if (this.coordinator is null)
        {
            return;
        }

        await this.coordinator.RefreshProviderAsync(key, cancellationToken);
        if (this.coordinator.States.FirstOrDefault(state => state.Key == key) is { } state)
        {
            this.LogProviderOutcome(state);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        this.logStore?.Error("Unhandled dispatcher exception", e.Exception);

        // Keep the tray alive: a failed refresh or menu rebuild must not take the app down.
        if (!this.hasReportedUnhandledError)
        {
            this.hasReportedUnhandledError = true;
            MessageBox.Show(
                $"CodexBar hit an unexpected error and kept running.\n\n{e.Exception.Message}",
                "CodexBar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        e.Handled = true;
    }
}
