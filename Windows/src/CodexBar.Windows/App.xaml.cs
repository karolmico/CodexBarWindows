using CodexBar.Core.Presentation;
using System.Windows;
using System.Windows.Threading;

namespace CodexBar.Windows;

public partial class App : Application
{
    private Tray.TrayHost? trayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

        try
        {
            this.trayHost = new Tray.TrayHost(() => TrayMenuBuilder.Build(
                providers: [],
                now: DateTimeOffset.Now,
                isRefreshing: false,
                settingsAvailable: false));

            this.trayHost.ExitRequested += (_, _) => this.Shutdown();
            this.trayHost.Start();
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
        this.trayHost?.Dispose();
        this.trayHost = null;
        base.OnExit(e);
    }

    private bool hasReportedUnhandledError;

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
