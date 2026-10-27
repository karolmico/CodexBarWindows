using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CodexBar.Core.Diagnostics;
using CodexBar.Core.Presentation;
using CodexBar.Core.Refresh;
using CodexBar.Core.Settings;

namespace CodexBar.Windows.Diagnostics;

/// <summary>Outcome of a copy-diagnostics request; the message is safe to show verbatim in the UI.</summary>
public sealed record DiagnosticsCopyResult(bool Copied, string Message, string? FilePath);

/// <summary>
/// Builds the redacted bundle, keeps a copy under the app data directory, and places it on the
/// clipboard. Clipboard ownership can fail transiently when another process is reading it, so the
/// copy is retried a few times.
/// </summary>
public sealed class DiagnosticsService
{
    private const int ClipboardAttempts = 4;

    private readonly string dataDirectory;

    public DiagnosticsService(string dataDirectory)
    {
        this.dataDirectory = dataDirectory
            ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    public DiagnosticsCopyResult CaptureAndCopy(
        AppSettings settings,
        IReadOnlyList<ProviderState> states,
        IReadOnlyList<ProviderTiming> timings,
        DateTimeOffset now)
    {
        var json = DiagnosticsBundle.Build(
            settings,
            states,
            timings,
            new DiagnosticsEnvironment(
                AppVersion(),
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription),
            now);

        string? filePath = null;
        try
        {
            Directory.CreateDirectory(this.dataDirectory);
            filePath = Path.Combine(this.dataDirectory, "diagnostics-latest.json");
            File.WriteAllText(filePath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            filePath = null;
        }

        for (var attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(json);
                return new DiagnosticsCopyResult(
                    Copied: true,
                    "Diagnostics copied to clipboard.",
                    filePath);
            }
            catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
            {
                if (attempt == ClipboardAttempts - 1)
                {
                    break;
                }

                Thread.Sleep(60);
            }
        }

        return new DiagnosticsCopyResult(
            Copied: false,
            filePath is null
                ? "Could not copy diagnostics."
                : "Couldn't use the clipboard; diagnostics were saved next to your settings.",
            filePath);
    }

    private static string AppVersion() =>
        typeof(DiagnosticsService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
