using System.Text.RegularExpressions;

namespace CodexBar.Core.Diagnostics;

/// <summary>
/// Removes secret-shaped values before anything reaches a log file or a diagnostics bundle. Applied to
/// whole lines and to the final JSON, so a missed field cannot leak a token. Patterns are deliberately
/// broad: over-redacting a log is cheaper than leaking a credential.
/// </summary>
public static class LogRedactor
{
    public const string Replacement = "[redacted]";
    public const string EmailReplacement = "[email]";
    public const string PathReplacement = "[path]";

    private static readonly Regex AuthorizationHeader = new(
        @"\b(?<prefix>authorization\s*[:=]\s*)(?<scheme>bearer|basic|token)\s+(?<value>[^\s,;""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CookieHeader = new(
        @"\b(?<prefix>(?:set-)?cookie\s*:\s*)[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SecretAssignment = new(
        @"(?<prefix>\b(?:access[_-]?token|refresh[_-]?token|id[_-]?token|auth[_-]?token|token|api[_-]?key|apikey|secret|password|passwd|session[_-]?key|csrf[_-]?token|cookie)\b""?\s*[:=]\s*""?)(?<value>[^""\s,;&}]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SecretQueryParameter = new(
        @"(?<prefix>[?&](?:access_token|refresh_token|id_token|token|api_key|apikey|key|code|signature|sig|password)=)(?<value>[^&\s#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Jwt = new(
        @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownTokenPrefix = new(
        @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{16,}|xox[baprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Email = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WindowsPath = new(
        @"\b[A-Za-z]:\\(?:[^\\\r\n""'<>|?*]+\\)*[^\\\r\n""'<>|?*]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UncPath = new(
        @"\\\\[A-Za-z0-9._-]+\\[^\r\n""'<>|?*]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnixHomePath = new(
        @"(?<prefix>/(?:Users|home)/)[^/\s""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = text;
        redacted = AuthorizationHeader.Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}{match.Groups["scheme"].Value} {Replacement}");
        redacted = CookieHeader.Replace(redacted, match => $"{match.Groups["prefix"].Value}{Replacement}");
        redacted = SecretAssignment.Replace(redacted, match => $"{match.Groups["prefix"].Value}{Replacement}");
        redacted = SecretQueryParameter.Replace(redacted, match => $"{match.Groups["prefix"].Value}{Replacement}");
        redacted = Jwt.Replace(redacted, Replacement);
        redacted = KnownTokenPrefix.Replace(redacted, Replacement);
        redacted = Email.Replace(redacted, EmailReplacement);
        redacted = WindowsPath.Replace(redacted, PathReplacement);
        redacted = UncPath.Replace(redacted, PathReplacement);
        redacted = UnixHomePath.Replace(redacted, match => $"{match.Groups["prefix"].Value}{PathReplacement}");
        return redacted;
    }
}
