using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CodexBar.Core.Providers.Antigravity;

namespace CodexBar.Providers.Windows;

/// <summary>
/// Runs <c>agy.exe</c> as a hidden child process for the Antigravity CLI usage report: no console
/// window, closed stdin (so a prompt cannot hang the fetch), bounded runtime, and a whole process
/// tree kill on timeout. The CLI's own auto-update is disabled so a refresh never installs anything.
/// </summary>
public sealed class WindowsAgyProcessRunner : IAgyProcessRunner
{
    private readonly Func<string?> resolveBinary;

    public WindowsAgyProcessRunner(Func<string?>? resolveBinary = null)
    {
        this.resolveBinary = resolveBinary ?? AgyBinaryLocator.Find;
    }

    public async Task<AgyRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The agy timeout must be positive.");
        }

        var binary = this.resolveBinary();
        if (binary is null)
        {
            return AgyRunResult.NotInstalled();
        }

        var startInfo = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("agy did not start.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            // An unusable binary is the same user problem as a missing one: install/repair the CLI.
            return AgyRunResult.NotInstalled();
        }

        using (process)
        {
            try
            {
                process.StandardInput.Close();

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);

                var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);

                return AgyRunResult.Completed(
                    process.ExitCode,
                    await stdout.ConfigureAwait(false),
                    await stderr.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                KillTree(process);
                return AgyRunResult.TimedOutAfter();
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                throw;
            }
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception
            or NotSupportedException)
        {
            // The process already exited, or Windows refused the kill; nothing else to do.
        }
    }
}

/// <summary>
/// Finds <c>agy.exe</c> the way a terminal would: first on <c>PATH</c>, then in the documented
/// per-user install directories.
/// </summary>
public static class AgyBinaryLocator
{
    public static string? Find()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Combine(directory.Trim().Trim('"'), "agy.exe");
            if (candidate is not null && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string?[] known =
        [
            string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "agy", "bin", "agy.exe"),
            string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "Programs", "agy", "agy.exe"),
            string.IsNullOrEmpty(userProfile) ? null : Path.Combine(userProfile, ".local", "bin", "agy.exe"),
        ];

        return known.FirstOrDefault(static candidate => candidate is not null && File.Exists(candidate));
    }

    private static string? Combine(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return Path.Combine(directory, fileName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
