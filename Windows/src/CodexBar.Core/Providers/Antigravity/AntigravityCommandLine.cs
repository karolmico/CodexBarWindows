using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexBar.Core.Providers.Antigravity;

/// <summary>One local Antigravity process as reported by the platform process table.</summary>
public sealed record AntigravityProcessInfo(int Pid, string CommandLine);

/// <summary>
/// Platform seam: enumerate language-server processes and the loopback ports one PID is listening on.
/// Windows supplies a WMI/TCP-table implementation; tests supply a fake.
/// </summary>
public interface IAntigravityProcessSource
{
    Task<IReadOnlyList<AntigravityProcessInfo>> ListProcessesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ListeningPortsAsync(int pid, CancellationToken cancellationToken);
}

public enum AntigravityProcessKind
{
    /// <summary>Antigravity 2.0 app language server. Authenticates with <c>--csrf_token</c>.</summary>
    App,

    /// <summary>Antigravity IDE extension language server. Authenticates with <c>--csrf_token</c>.</summary>
    Ide,

    /// <summary>Antigravity CLI (<c>agy</c>) language server. Needs no CSRF token.</summary>
    Cli,
}

/// <summary>A process the probe can talk to, with the token and port flags parsed from its command line.</summary>
public sealed record AntigravityEndpoint(
    int Pid,
    AntigravityProcessKind Kind,
    string? CsrfToken,
    int? ExtensionServerPort);

/// <summary>
/// Classifies Antigravity language-server command lines. Mirrors the upstream matcher: a language
/// server whose command line also names Antigravity, the IDE variant, or the <c>agy</c> CLI.
/// </summary>
public static partial class AntigravityCommandLine
{
    public static AntigravityEndpoint? Parse(AntigravityProcessInfo process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var commandLine = process.CommandLine;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var lower = commandLine.ToLowerInvariant();
        var kind = Classify(lower);
        if (kind is null)
        {
            return null;
        }

        var token = ExtractFlag(commandLine, "--csrf_token");
        var extensionPort = int.TryParse(
            ExtractFlag(commandLine, "--extension_server_port"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port) && port is > 0 and <= 65535
            ? port
            : (int?)null;

        return new AntigravityEndpoint(process.Pid, kind.Value, token, extensionPort);
    }

    public static AntigravityProcessKind? Classify(string lowerCommandLine)
    {
        if (LanguageServerPattern().IsMatch(lowerCommandLine) && IsAntigravityPath(lowerCommandLine))
        {
            return IsIdePath(lowerCommandLine) ? AntigravityProcessKind.Ide : AntigravityProcessKind.App;
        }

        return CliPattern().IsMatch(lowerCommandLine) ? AntigravityProcessKind.Cli : null;
    }

    private static bool IsAntigravityPath(string lower) =>
        lower.Contains("--app_data_dir") && lower.Contains("antigravity")
        || lower.Contains("antigravity.app/")
        || lower.Contains("antigravity.app\\")
        || lower.Contains("antigravity ide.app/")
        || lower.Contains("antigravity ide.app\\")
        || lower.Contains("/antigravity/")
        || lower.Contains("\\antigravity\\")
        || lower.Contains("/gemini.app/")
        || lower.Contains("\\gemini.app\\");

    private static bool IsIdePath(string lower) =>
        lower.Contains("antigravity ide.app/")
        || lower.Contains("antigravity ide.app\\")
        || lower.Contains("--app_data_dir antigravity-ide")
        || lower.Contains("--app_data_dir=antigravity-ide")
        || lower.Contains("/extensions/antigravity/bin/language_server")
        || lower.Contains("\\extensions\\antigravity\\bin\\language_server");

    private static string? ExtractFlag(string commandLine, string flag)
    {
        var match = Regex.Match(
            commandLine,
            Regex.Escape(flag) + @"[=\s]+([^\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }

    [GeneratedRegex(@"(^|[/\\])language(?:_|-)server(?:[_-][a-z0-9]+)*(?:\.exe)?[\""']?(\s|$)")]
    private static partial Regex LanguageServerPattern();

    [GeneratedRegex(@"^(.*[/\\])?(antigravity-cli|antigravity_cli|agy)(\.exe)?(\s|$)")]
    private static partial Regex CliPattern();
}
