using System.Windows;
using CodexBar.Core.Settings;
using Microsoft.Win32;

namespace CodexBar.Windows;

/// <summary>
/// Swaps the flyout palette in and out of the application resources. DynamicResource references pick the
/// change up live, so a theme switch from Settings repaints any open window without a restart.
/// </summary>
internal static class ThemeManager
{
    private const string LightPath = "/CodexBar.Windows;component/Theme.Light.xaml";
    private const string DarkPath = "/CodexBar.Windows;component/Theme.Dark.xaml";

    public static void Apply(AppTheme theme)
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            if (resources[index].Source?.OriginalString.Contains("/Theme.", StringComparison.Ordinal) == true)
            {
                resources.RemoveAt(index);
            }
        }

        resources.Add(new ResourceDictionary
        {
            Source = new Uri(IsDark(theme) ? DarkPath : LightPath, UriKind.Relative),
        });
    }

    public static bool IsDark(AppTheme theme) =>
        theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => SystemUsesDarkTheme(),
        };

    /// <summary>Reads the per-user "Apps use dark mode" toggle; light is the safe default.</summary>
    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or ObjectDisposedException)
        {
            return false;
        }
    }
}
