using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Refresh;

namespace CodexBar.Windows;

public partial class App : Application
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer refreshTimer = new(DispatcherPriority.Background);
    private readonly CancellationTokenSource shutdown = new();
    private UsageCoordinator? coordinator;
    private Tray.TrayHost? trayHost;
    private bool hasReportedUnhandledError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

        try
        {
            var codex = new CodexProvider(new CodexAuthFileStore(), new CodexUsageClient(this.httpClient));
            this.coordinator = new UsageCoordinator([codex], new RefreshPolicy(), () => DateTimeOffset.Now);
            this.coordinator.Changed += this.OnUsageChanged;

            this.refreshTimer.Tick += this.OnRefreshTimerTick;

            this.trayHost = new Tray.TrayHost(this.BuildMenu);
            this.trayHost.ExitRequested += (_, _) => this.Shutdown();
            this.trayHost.RefreshRequested += (_, _) => _ = this.RefreshAsync();
            this.trayHost.Start();

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

    private TrayMenuModel BuildMenu() =>
        this.coordinator is null
            ? TrayMenuBuilder.Build([], DateTimeOffset.Now, isRefreshing: false)
            : TrayMenuBuilder.Build(
                this.coordinator.States,
                DateTimeOffset.Now,
                this.coordinator.IsRefreshing,
                settingsAvailable: false);

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

        var delay = this.coordinator.NextDelay();
        this.refreshTimer.Interval = delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
        this.refreshTimer.Start();
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
