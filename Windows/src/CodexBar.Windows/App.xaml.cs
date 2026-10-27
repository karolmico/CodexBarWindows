using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CodexBar.Core.Persistence;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Claude;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Providers.Copilot;
using CodexBar.Core.Providers.Cursor;
using CodexBar.Core.Providers.Gemini;
using CodexBar.Core.Refresh;
using CodexBar.Core.Settings;

namespace CodexBar.Windows;

public partial class App : Application
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer refreshTimer = new(DispatcherPriority.Background);
    private readonly CancellationTokenSource shutdown = new();
    private ProviderRegistry registry = null!;
    private SettingsStore settingsStore = null!;
    private SnapshotCache snapshotCache = null!;
    private AppSettings settings = null!;
    private UsageCoordinator? coordinator;
    private Tray.TrayHost? trayHost;
    private bool hasReportedUnhandledError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CodexBar");

            // Provider catalog. New providers are added here; enablement and order come from settings.
            this.registry = new ProviderRegistry(
            [
                new CodexProvider(new CodexAuthFileStore(), new CodexUsageClient(this.httpClient)),
                new ClaudeProvider(new ClaudeAuthFileStore(), new ClaudeUsageClient(this.httpClient)),
                new GeminiProvider(new GeminiAuthFileStore(), new GeminiUsageClient(this.httpClient)),
                new CopilotProvider(
                    new CopilotTokenSource(),
                    new CopilotUsageClient(this.httpClient)),
                new CursorProvider(
                    new CursorSessionStore(),
                    new CursorUsageClient(this.httpClient)),
            ]);

            this.settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
            this.snapshotCache = new SnapshotCache(Path.Combine(dataDirectory, "snapshots.json"));

            var loaded = this.settingsStore.Load(
                AppSettings.DefaultFor(this.registry.All.Select(provider => provider.Descriptor)));
            this.settings = loaded.Settings;

            this.coordinator = this.CreateCoordinator(this.settings);
            this.coordinator.ApplyCached(this.snapshotCache.Load());

            this.refreshTimer.Tick += this.OnRefreshTimerTick;

            this.trayHost = new Tray.TrayHost(this.BuildMenu);
            this.trayHost.ExitRequested += (_, _) => this.Shutdown();
            this.trayHost.RefreshRequested += (_, _) => _ = this.RefreshAsync();
            this.trayHost.SettingsRequested += (_, _) => this.ShowSettings();
            this.trayHost.Start();

            if (loaded.Warning is { } warning)
            {
                this.trayHost.Notify("CodexBar settings", warning, isWarning: true);
            }

            _ = this.RefreshAsync();
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
        this.trayHost?.Dispose();
        this.trayHost = null;
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

    private void Render() => this.trayHost?.Render(this.BuildMenu());

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
        this.PersistSnapshots();

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

    private void ShowSettings()
    {
        var window = new SettingsWindow(
            this.settings,
            this.registry.All.Select(provider => provider.Descriptor).ToArray())
        {
            Owner = null,
        };

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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
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
