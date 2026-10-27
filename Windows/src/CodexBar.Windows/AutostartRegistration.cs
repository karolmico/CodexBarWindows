using System.IO;
using Microsoft.Win32;

namespace CodexBar.Windows;

/// <summary>
/// Owns the per-user Run entry. There is no machine-wide registration and no elevation: everything
/// lives under <c>HKCU</c>. Development runs through <c>dotnet</c>, so they are a no-op and can never
/// point the Run key at the SDK host.
/// </summary>
public static class AutostartRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexBar";
    private const string HostFileName = "CodexBar.Windows.exe";

    /// <summary>Creates or removes the entry to match the setting. Failures stay silent.</summary>
    public static void Apply(bool enabled)
    {
        var executable = ExecutablePath();
        if (executable is null)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            // Autostart is a convenience; a locked registry must not take the tray down.
        }
    }

    /// <summary>Published apphost path, or null when running under the SDK host.</summary>
    public static string? ExecutablePath()
    {
        var path = Environment.ProcessPath;
        return path is not null
            && string.Equals(Path.GetFileName(path), HostFileName, StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
    }
}
