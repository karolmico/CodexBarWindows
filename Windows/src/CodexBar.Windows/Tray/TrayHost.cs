using System.Drawing;
using System.IO;
using System.Windows.Controls;
using CodexBar.Core.Presentation;
using H.NotifyIcon;

namespace CodexBar.Windows.Tray;

/// <summary>
/// Owns the notification-area icon and renders the core's tray menu model onto a WPF context menu.
/// All decisions about what to show live in <see cref="TrayMenuBuilder"/>; this type only wires input.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly Func<TrayMenuModel> modelFactory;
    private readonly TaskbarIcon taskbarIcon;
    private Icon? icon;
    private bool disposed;

    public TrayHost(Func<TrayMenuModel> modelFactory)
    {
        this.modelFactory = modelFactory ?? throw new ArgumentNullException(nameof(modelFactory));
        this.taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "CodexBar",
        };
        this.taskbarIcon.TrayContextMenuOpen += this.OnContextMenuOpen;
        this.taskbarIcon.TrayLeftMouseDoubleClick += this.OnDoubleClick;
    }

    public event EventHandler? ExitRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ShowFlyoutRequested;

    public void Start()
    {
        this.icon = LoadIcon();
        this.taskbarIcon.Icon = this.icon;
        this.taskbarIcon.ForceCreate(enablesEfficiencyMode: true);
        this.Render(this.modelFactory());
    }

    /// <summary>Shows a tray balloon; used for recoverable startup warnings.</summary>
    public void Notify(string title, string message, bool isWarning)
    {
        this.taskbarIcon.ShowNotification(
            title,
            message,
            isWarning ? H.NotifyIcon.Core.NotificationIcon.Warning : H.NotifyIcon.Core.NotificationIcon.Info);
    }

    /// <summary>Re-renders tooltip and menu from the current model, e.g. after a refresh completes.</summary>
    public void Render(TrayMenuModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.taskbarIcon.ToolTipText = model.ToolTip;
        this.taskbarIcon.ContextMenu = this.BuildContextMenu(model);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.taskbarIcon.TrayContextMenuOpen -= this.OnContextMenuOpen;
        this.taskbarIcon.TrayLeftMouseDoubleClick -= this.OnDoubleClick;
        this.taskbarIcon.Dispose();
        this.icon?.Dispose();
    }

    private static Icon LoadIcon()
    {
        // Copied next to the executable by the build; avoids pack-URI decoding at startup.
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "CodexBar.ico");
        return new Icon(path);
    }

    private ContextMenu BuildContextMenu(TrayMenuModel model)
    {
        var menu = new ContextMenu();
        foreach (var entry in model.Entries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Label,
                IsEnabled = entry.IsEnabled,
                Tag = entry.Id,
            };
            item.Click += this.OnMenuItemClick;
            menu.Items.Add(item);
        }

        return menu;
    }

    private void OnMenuItemClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id })
        {
            return;
        }

        switch (id)
        {
            case TrayMenuBuilder.ExitId:
                this.ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayMenuBuilder.RefreshId:
                this.RefreshRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayMenuBuilder.SettingsId:
                this.SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (id.StartsWith("provider:", StringComparison.Ordinal))
                {
                    this.ShowFlyoutRequested?.Invoke(this, EventArgs.Empty);
                }

                break;
        }
    }

    private void OnContextMenuOpen(object sender, System.Windows.RoutedEventArgs e) =>
        this.Render(this.modelFactory());

    private void OnDoubleClick(object sender, System.Windows.RoutedEventArgs e) =>
        this.ShowFlyoutRequested?.Invoke(this, EventArgs.Empty);
}
