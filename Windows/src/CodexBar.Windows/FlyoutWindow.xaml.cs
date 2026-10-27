using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexBar.Core.Presentation;

namespace CodexBar.Windows;

/// <summary>
/// Borderless flyout anchored above the notification area. It only renders a <see cref="FlyoutModel"/>;
/// refresh and settings intentions leave through events so the composition root stays in charge.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double ScreenMargin = 8;
    private readonly DispatcherTimer diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool isClosing;

    public FlyoutWindow(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.InitializeComponent();
        this.DataContext = model;
        this.Loaded += (_, _) => this.FocusHighlightedCard();
        this.diagnosticsTimer.Tick += (_, _) =>
        {
            this.diagnosticsTimer.Stop();
            this.DiagnosticsStatus.Visibility = Visibility.Collapsed;
        };
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? DiagnosticsRequested;

    /// <summary>Raised with a provider key when the user asks to connect a provider that failed.</summary>
    public event EventHandler<string>? ProviderSetupRequested;

    /// <summary>Inline confirmation for the copy action; no toast dependency, so it always shows.</summary>
    public void ShowDiagnosticsStatus(string message, bool isError)
    {
        this.DiagnosticsStatus.Text = message;
        this.DiagnosticsStatus.Foreground = isError ? Brushes.Firebrick : Brushes.SeaGreen;
        this.DiagnosticsStatus.Visibility = Visibility.Visible;
        this.diagnosticsTimer.Stop();
        this.diagnosticsTimer.Start();
    }

    public void Update(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        this.DataContext = model;
    }

    /// <summary>
    /// Shows the flyout just above the taskbar, horizontally near the mouse pointer. The window is
    /// laid out at zero opacity first so its natural height is known before it is placed.
    /// </summary>
    public void ShowAtNotificationArea()
    {
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

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        this.RefreshRequested?.Invoke(this, EventArgs.Empty);
        this.CloseOnce();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        this.SettingsRequested?.Invoke(this, EventArgs.Empty);
        this.CloseOnce();
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

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e) =>
        this.DiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeactivated(object? sender, EventArgs e) => this.CloseOnce();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.CloseOnce();
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

    private void FocusHighlightedCard()
    {
        if (this.DataContext is not FlyoutModel model
            || model.Providers.FirstOrDefault(static provider => provider.IsHighlighted) is not { } highlighted)
        {
            return;
        }

        if (this.FindName("ProviderList") is FrameworkElement list
            && list is System.Windows.Controls.ItemsControl items
            && items.ItemContainerGenerator.ContainerFromItem(highlighted) is FrameworkElement container)
        {
            container.BringIntoView();
            container.Focus();
        }
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
