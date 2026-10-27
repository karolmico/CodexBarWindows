using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using CodexBar.Core.Presentation;

namespace CodexBar.Windows;

/// <summary>
/// Borderless flyout anchored above the notification area. It only renders a <see cref="FlyoutModel"/>;
/// refresh and settings intentions leave through events so the composition root stays in charge.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double ScreenMargin = 8;
    private bool isClosing;

    public FlyoutWindow(FlyoutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.InitializeComponent();
        this.DataContext = model;
        this.Loaded += (_, _) => this.FocusHighlightedCard();
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? SettingsRequested;

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
