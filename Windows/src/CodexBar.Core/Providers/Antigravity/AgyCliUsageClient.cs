using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Antigravity;

/// <summary>Outcome of one <c>agy</c> run as reported by the platform process runner.</summary>
public sealed record AgyRunResult(bool Started, bool TimedOut, int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>No <c>agy</c> executable was found on this PC.</summary>
    public static AgyRunResult NotInstalled() => new(false, false, 0, string.Empty, string.Empty);

    public static AgyRunResult TimedOutAfter() =>
        new(true, true, 0, string.Empty, string.Empty);

    public static AgyRunResult Completed(int exitCode, string standardOutput, string standardError) =>
        new(true, false, exitCode, standardOutput ?? string.Empty, standardError ?? string.Empty);
}

/// <summary>
/// Platform seam for running the Antigravity CLI. Windows supplies a hidden-process runner;
/// tests supply a scripted fake so no test ever launches the real binary.
/// </summary>
public interface IAgyProcessRunner
{
    Task<AgyRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional second Antigravity source. A null result means "no agy on this PC", which lets the
/// provider fall back to its own not-installed copy instead of claiming a fetch failure.
/// </summary>
public interface IAntigravityCliSource
{
    Task<UsageFetchResult?> TryFetchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads Antigravity quota through the <c>agy</c> CLI print report, so the tray keeps working with
/// the desktop app closed. Mirrors upstream: <c>agy -p "/usage" --output-format json</c>. The CLI
/// boots a whole language server, so the version probe is cached for the life of the process and
/// every run is bounded by a timeout.
/// </summary>
public sealed class AgyCliUsageClient : IAntigravityCliSource
{
    /// <summary>First release whose print report is a JSON usage document instead of model output.</summary>
    public static readonly Version MinimumVersion = new(1, 1, 11);

    /// <summary>The exact upstream print invocation; <c>-p</c> is the CLI's one-shot prompt flag.</summary>
    public static readonly IReadOnlyList<string> UsageArguments =
        ["-p", "/usage", "--output-format", "json", "--print-timeout", "90s"];

    private readonly IAgyProcessRunner runner;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan versionTimeout;
    private readonly TimeSpan usageTimeout;
    private readonly SemaphoreSlim versionGate = new(1, 1);
    private Version? cachedVersion;

    public AgyCliUsageClient(
        IAgyProcessRunner runner,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? versionTimeout = null,
        TimeSpan? usageTimeout = null)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.versionTimeout = versionTimeout ?? TimeSpan.FromSeconds(5);
        this.usageTimeout = usageTimeout ?? TimeSpan.FromSeconds(60);
    }

    public async Task<UsageFetchResult?> TryFetchAsync(CancellationToken cancellationToken)
    {
        var (version, versionError) = await this.ResolveVersionAsync(cancellationToken).ConfigureAwait(false);
        if (versionError is not null)
        {
            return versionError;
        }

        if (version is null)
        {
            return null;
        }

        if (version < MinimumVersion)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Antigravity CLI {version} is too old for usage reports. Update agy to {MinimumVersion} or later.")));
        }

        var run = await this.runner
            .RunAsync(UsageArguments, this.usageTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!run.Started)
        {
            return null;
        }

        if (run.TimedOut)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "The Antigravity CLI did not answer in time. Will retry shortly."));
        }

        if (run.ExitCode != 0)
        {
            return Failure(ClassifyExit(run));
        }

        return ParseReport(run.StandardOutput, this.clock());
    }

    private async Task<(Version? Version, UsageFetchResult? Error)> ResolveVersionAsync(
        CancellationToken cancellationToken)
    {
        if (this.cachedVersion is { } cached)
        {
            return (cached, null);
        }

        await this.versionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.cachedVersion is { } raced)
            {
                return (raced, null);
            }

            var run = await this.runner
                .RunAsync(["--version"], this.versionTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!run.Started)
            {
                return (null, null);
            }

            if (run.TimedOut)
            {
                return (null, Failure(new ProviderError(
                    ProviderErrorKind.Network,
                    "The Antigravity CLI did not answer its version check. Will retry shortly.")));
            }

            if (run.ExitCode != 0)
            {
                return (null, Failure(ClassifyExit(run)));
            }

            var version = ParseVersion(run.StandardOutput);
            if (version is null)
            {
                return (null, Failure(new ProviderError(
                    ProviderErrorKind.Parse,
                    "Could not read the Antigravity CLI version. Will retry shortly.")));
            }

            this.cachedVersion = version;
            return (version, null);
        }
        finally
        {
            this.versionGate.Release();
        }
    }

    /// <summary>Accepts a plain <c>major.minor.patch</c> line, as upstream does.</summary>
    public static Version? ParseVersion(string? output)
    {
        var text = output?.Trim();
        if (string.IsNullOrEmpty(text)
            || !Regex.IsMatch(text, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        return Version.TryParse(text, out var version) ? version : null;
    }

    private static UsageFetchResult ParseReport(string stdout, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(ExtractJsonObject(stdout));
        }
        catch (JsonException)
        {
            return ParseError();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String
                || !string.Equals(status.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("command", out var command)
                || command.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(command, "name"), "usage", StringComparison.OrdinalIgnoreCase)
                || !command.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("groups", out var groups)
                || groups.ValueKind != JsonValueKind.Array)
            {
                return ParseError();
            }

            var lanes = new List<AntigravityQuotaLane>();
            foreach (var group in groups.EnumerateArray())
            {
                ReadGroup(group, lanes);
            }

            if (!lanes.Any(static lane => lane.UsageKnown))
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.Parse,
                    "The Antigravity CLI reported no usable quota. Will retry shortly."));
            }

            return new UsageFetchResult.Success(AntigravityQuotaSnapshot.Build(lanes, now));
        }
    }

    private static void ReadGroup(JsonElement group, List<AntigravityQuotaLane> lanes)
    {
        if (group.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var groupName = ReadFirst(group, "display_name", "displayName", "name") ?? "Quota";
        if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (bucket.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var bucketId = ReadFirst(bucket, "id", "bucket_id", "bucketId");
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                continue;
            }

            var bucketName = ReadFirst(bucket, "display_name", "displayName", "name") ?? bucketId;
            var disabled = bucket.TryGetProperty("disabled", out var flag)
                && flag.ValueKind == JsonValueKind.True;
            var fraction = ReadFraction(bucket);
            var cadence = AntigravityQuotaSnapshot.ClassifyCadence(
                ReadFirst(bucket, "window"),
                bucketId,
                bucketName);

            lanes.Add(new AntigravityQuotaLane(
                groupName,
                bucketId,
                bucketName,
                cadence,
                fraction,
                ParseDate(ReadFirst(bucket, "reset_time", "resetTime")),
                !disabled && fraction is not null,
                disabled,
                ReadFirst(bucket, "description")));
        }
    }

    private static double? ReadFraction(JsonElement bucket)
    {
        if (TryReadNumber(bucket, out var direct, "remaining_fraction", "remainingFraction"))
        {
            return direct;
        }

        if (!bucket.TryGetProperty("remaining", out var remaining) || remaining.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryReadNumber(remaining, out var nested, "remaining_fraction", "remainingFraction"))
        {
            return nested;
        }

        return remaining.TryGetProperty("case", out var oneOf)
            && oneOf.ValueKind == JsonValueKind.String
            && oneOf.GetString() == "remainingFraction"
            && remaining.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : null;
    }

    private static bool TryReadNumber(JsonElement element, out double? value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                value = property.GetDouble();
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Upstream treats any non-zero exit as a CLI failure. The CLI's stderr can carry sign-in
    /// prompts, proxy hosts and local paths, so it is classified into fixed copy and never shown.
    /// </summary>
    private static ProviderError ClassifyExit(AgyRunResult run)
    {
        var text = run.StandardError.ToLowerInvariant();
        if (AuthenticationMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return new ProviderError(
                ProviderErrorKind.AuthenticationRequired,
                "The Antigravity CLI isn't signed in. Run `agy` in a terminal, sign in, and refresh.");
        }

        if (NetworkMarkers.Any(marker => marker.IsMatch(text)))
        {
            return new ProviderError(
                ProviderErrorKind.Network,
                "The Antigravity CLI couldn't reach Antigravity. Check the network or proxy and try again.");
        }

        return new ProviderError(
            ProviderErrorKind.Unknown,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The Antigravity CLI exited with code {run.ExitCode}. Run `agy` in a terminal to check the sign-in."));
    }

    /// <summary>Keeps a leading update banner or trailing newline from breaking the report.</summary>
    private static string ExtractJsonObject(string stdout)
    {
        var text = stdout.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static string? ReadFirst(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (ReadString(element, name) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    private static UsageFetchResult ParseError() =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "The Antigravity CLI returned an unreadable usage report. Will retry shortly."));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);

    private static readonly string[] AuthenticationMarkers =
    [
        "select login method",
        "not logged in",
        "not signed in",
        "not logged into antigravity",
        "unauthenticated",
        "authentication required",
        "login required",
        "please log in",
        "please sign in",
    ];

    private static readonly Regex[] NetworkMarkers =
    [
        new(@"\beof\b", RegexOptions.CultureInvariant),
        new(@"\btimed out\b", RegexOptions.CultureInvariant),
        new(@"\btimeout\b", RegexOptions.CultureInvariant),
        new(@"\bdeadline exceeded\b", RegexOptions.CultureInvariant),
        new(@"\bi/o timeout\b", RegexOptions.CultureInvariant),
        new(@"\bno such host\b", RegexOptions.CultureInvariant),
        new(@"\bconnection refused\b", RegexOptions.CultureInvariant),
        new(@"\bconnection reset\b", RegexOptions.CultureInvariant),
        new(@"\bnetwork is unreachable\b", RegexOptions.CultureInvariant),
        new(@"\bunreachable\b", RegexOptions.CultureInvariant),
        new(@"\bproxy\b", RegexOptions.CultureInvariant),
    ];
}
