using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexBar.Core.Presentation;

namespace CodexBar.Windows;

/// <summary>
/// Borderless flyout anchored above the notification area. It renders a <see cref="FlyoutModel"/>
/// through <see cref="FlyoutViewModel"/>, which owns the selected tab; refresh, settings, and setup
/// intentions leave through events so the composition root stays in charge.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double ScreenMargin = 8;
    private readonly DispatcherTimer diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly FlyoutViewModel viewModel;
    private bool isClosing;

    public FlyoutWindow(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.InitializeComponent();
        this.viewModel = new FlyoutViewModel(model);
        this.DataContext = this.viewModel;
        this.diagnosticsTimer.Tick += (_, _) =>
        {
            this.diagnosticsTimer.Stop();
            this.DiagnosticsStatus.Visibility = Visibility.Collapsed;
        };
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? AboutRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? DiagnosticsRequested;

    /// <summary>Raised with a provider key when the user asks to configure the selected provider.</summary>
    public event EventHandler<string>? ProviderSetupRequested;

    /// <summary>Raised with an absolute http(s) address the user asked to open.</summary>
    public event EventHandler<string>? LinkRequested;

    /// <summary>Inline confirmation for the copy action; no toast dependency, so it always shows.</summary>
    public void ShowDiagnosticsStatus(string message, bool isError)
    {
        this.DiagnosticsStatus.Text = message;
        this.DiagnosticsStatus.Foreground = isError ? Brushes.Firebrick : Brushes.SeaGreen;
        this.DiagnosticsStatus.Visibility = Visibility.Visible;
        this.diagnosticsTimer.Stop();
        this.diagnosticsTimer.Start();
    }

    /// <summary>Re-projects provider state without losing the tab the user is reading.</summary>
    public void Update(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        this.viewModel.Apply(model);
    }

    /// <summary>
    /// Shows the flyout just above the taskbar, horizontally near the mouse pointer. Height is first
    /// measured at the fixed width and clamped to the work area, so the window grows to its content and
    /// only the detail pane scrolls when the content really cannot fit.
    /// </summary>
    public void ShowAtNotificationArea()
    {
        // Cap the height to the work area; SizeToContent grows the window to its content and the
        // star-row ScrollViewer scrolls only once that cap is reached.
        this.MaxHeight = Math.Max(220d, SystemParameters.WorkArea.Height - (ScreenMargin * 2));

        this.Opacity = 0;
        this.Show();
        this.UpdateLayout();
        this.PlaceNearNotificationArea();
        this.Opacity = 1;
        this.Activate();
    }

    private void PlaceNearNotificationArea()
    {
        var area = SystemParameters.WorkArea;
        var pointer = CursorPosition();

        var width = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
        var height = this.ActualHeight > 0 ? this.ActualHeight : 0;
        var left = Math.Clamp(
            pointer.X - (width / 2),
            area.Left + ScreenMargin,
            Math.Max(area.Left + ScreenMargin, area.Right - width - ScreenMargin));
        var top = Math.Max(area.Top + ScreenMargin, area.Bottom - height - ScreenMargin);

        this.Left = left;
        this.Top = top;
    }

    private void OnSelectProvider(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            this.viewModel.Select(key);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        this.CloseOnce();
        this.RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        this.CloseOnce();
        this.SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        this.CloseOnce();
        this.AboutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        this.CloseOnce();
        this.ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfigureProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key } || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Close first so the setup dialog is not opened behind a topmost flyout that then
        // deactivates and fights it for focus.
        this.CloseOnce();
        this.ProviderSetupRequested?.Invoke(this, key);
    }

    private void OnOpenDashboard(object sender, RoutedEventArgs e) =>
        this.Open(this.viewModel.Selected?.Links.DashboardUrl);

    private void OnOpenStatusPage(object sender, RoutedEventArgs e) =>
        this.Open(this.viewModel.Selected?.Links.StatusUrl);

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e) =>
        this.DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    private void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        this.CloseOnce();
        this.LinkRequested?.Invoke(this, url);
    }

    private void OnDeactivated(object? sender, EventArgs e) => this.CloseOnce();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                this.CloseOnce();
                break;
            case Key.Left:
                e.Handled = this.viewModel.SelectAdjacent(-1);
                break;
            case Key.Right:
                e.Handled = this.viewModel.SelectAdjacent(1);
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Deactivated also fires while the window is tearing down; never re-enter Close().
        this.isClosing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        this.diagnosticsTimer.Stop();
        base.OnClosed(e);
    }

    private void CloseOnce()
    {
        if (this.isClosing)
        {
            return;
        }

        this.isClosing = true;
        this.Close();
    }

    private static Point CursorPosition()
    {
        return GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(0, 0);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
