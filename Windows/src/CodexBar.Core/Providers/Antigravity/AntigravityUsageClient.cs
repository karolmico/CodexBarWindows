using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Antigravity;

/// <summary>
/// Probes the local Antigravity language server over loopback. The server speaks JSON over HTTP
/// (Connect protocol) and authenticates IDE/app requests with a CSRF token from the process
/// command line. Every attempt is bounded by its own timeout so a wedged server cannot stall refresh.
/// </summary>
public sealed class AntigravityUsageClient
{
    public const string QuotaSummaryPath =
        "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";

    public const string UserStatusPath =
        "/exa.language_server_pb.LanguageServerService/GetUserStatus";

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan attemptTimeout;

    public AntigravityUsageClient(
        HttpClient httpClient,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? attemptTimeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Ports to try, most specific first: the extension server, then loopback listeners.</summary>
    public static IReadOnlyList<int> CandidatePorts(AntigravityEndpoint endpoint, IReadOnlyList<int> listeningPorts)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(listeningPorts);

        var ports = new List<int>();
        if (endpoint.ExtensionServerPort is { } extensionPort)
        {
            ports.Add(extensionPort);
        }

        foreach (var port in listeningPorts)
        {
            if (!ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    public async Task<UsageFetchResult> FetchAsync(
        AntigravityEndpoint endpoint,
        IReadOnlyList<int> listeningPorts,
        CancellationToken cancellationToken)
    {
        var ports = CandidatePorts(endpoint, listeningPorts);
        if (ports.Count == 0)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "Antigravity is running but has not opened a usage port yet. Try again in a few seconds."));
        }

        UsageFetchResult? last = null;
        foreach (var port in ports)
        {
            var result = await this.TryFetchAsync(endpoint, port, cancellationToken).ConfigureAwait(false);
            if (result is UsageFetchResult.Success)
            {
                return result;
            }

            last = result;
        }

        return last!;
    }

    private async Task<UsageFetchResult> TryFetchAsync(
        AntigravityEndpoint endpoint,
        int port,
        CancellationToken cancellationToken)
    {
        var quota = await this.SendAsync(
            endpoint,
            port,
            QuotaSummaryPath,
            "{\"forceRefresh\":true}",
            cancellationToken).ConfigureAwait(false);
        if (quota.Error is not null)
        {
            return Failure(quota.Error);
        }

        var parsed = ParseQuotaSummary(quota.Body!, this.clock());
        if (parsed.Error is not null)
        {
            return Failure(parsed.Error);
        }

        var identity = await this.TryFetchIdentityAsync(endpoint, port, cancellationToken).ConfigureAwait(false);
        var snapshot = parsed.Snapshot!;
        return new UsageFetchResult.Success(identity is null
            ? snapshot
            : new UsageSnapshot(
                snapshot.Provider,
                snapshot.UpdatedAt,
                snapshot.Primary,
                snapshot.Secondary,
                snapshot.ExtraWindows,
                identity));
    }

    private async Task<ProviderIdentity?> TryFetchIdentityAsync(
        AntigravityEndpoint endpoint,
        int port,
        CancellationToken cancellationToken)
    {
        const string Body =
            "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\","
            + "\"ideVersion\":\"unknown\",\"locale\":\"en\"}}";

        var response = await this.SendAsync(endpoint, port, UserStatusPath, Body, cancellationToken)
            .ConfigureAwait(false);
        if (response.Error is not null || response.Body is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            if (!document.RootElement.TryGetProperty("userStatus", out var status)
                || status.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var email = ReadString(status, "email");
            var plan = status.TryGetProperty("userTier", out var tier) && tier.ValueKind == JsonValueKind.Object
                ? ReadString(tier, "name")
                : null;
            if (plan is null
                && status.TryGetProperty("planStatus", out var planStatus)
                && planStatus.ValueKind == JsonValueKind.Object
                && planStatus.TryGetProperty("planInfo", out var planInfo)
                && planInfo.ValueKind == JsonValueKind.Object)
            {
                plan = ReadFirst(planInfo, "planDisplayName", "displayName", "productName", "planName", "planShortName");
            }

            return MakeIdentity(email, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderIdentity? MakeIdentity(string? email, string? plan) =>
        email is null && plan is null ? null : new ProviderIdentity(plan, email);

    private async Task<(string? Body, ProviderError? Error)> SendAsync(
        AntigravityEndpoint endpoint,
        int port,
        string path,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{path}")))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        if (endpoint.CsrfToken is { Length: > 0 } token)
        {
            request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(this.attemptTimeout);

        try
        {
            using var response = await this.httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return (null, ProviderError.AuthenticationRequired(
                    "Antigravity rejected the local session. Restart Antigravity and try again."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return (null, new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Antigravity returned HTTP {(int)response.StatusCode}.")));
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return (null, new ProviderError(
                ProviderErrorKind.Network,
                "Antigravity did not answer in time. Will retry shortly.",
                cause: exception));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return (null, new ProviderError(
                ProviderErrorKind.Network,
                "Could not reach the Antigravity language server. Will retry shortly.",
                cause: exception));
        }
    }

    // MARK: - Parsing

    private static (UsageSnapshot? Snapshot, ProviderError? Error) ParseQuotaSummary(
        string body,
        DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, ParseError());
            }

            if (root.TryGetProperty("code", out var code) && !IsOkCode(code))
            {
                return (null, new ProviderError(
                    ProviderErrorKind.Parse,
                    "Antigravity reported an error retrieving quota. Will retry shortly."));
            }

            if (!TryGetPayload(root, out var payload)
                || !payload.TryGetProperty("groups", out var groups)
                || groups.ValueKind != JsonValueKind.Array)
            {
                return (null, ParseError());
            }

            var windows = new List<QuotaWindow>();
            foreach (var group in groups.EnumerateArray())
            {
                ReadGroup(group, windows);
            }

            if (windows.Count == 0 || !windows.Any(static window => window.UsageKnown))
            {
                return (null, new ProviderError(
                    ProviderErrorKind.Parse,
                    "Antigravity reported no usable quota buckets. Will retry shortly."));
            }

            return (BuildSnapshot(windows, now), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentOutOfRangeException)
        {
            return (null, ParseError(exception));
        }
    }

    private static bool TryGetPayload(JsonElement root, out JsonElement payload)
    {
        foreach (var name in new[] { "response", "summary" })
        {
            if (root.TryGetProperty(name, out payload) && payload.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        payload = root;
        return payload.TryGetProperty("groups", out _);
    }

    private static void ReadGroup(JsonElement group, List<QuotaWindow> windows)
    {
        if (group.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var groupName = ReadString(group, "displayName") ?? ReadString(group, "name") ?? "Quota";
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

            var bucketId = ReadString(bucket, "bucketId") ?? ReadString(bucket, "id");
            if (string.IsNullOrWhiteSpace(bucketId))
            {
                continue;
            }

            var bucketName = ReadString(bucket, "displayName") ?? ReadString(bucket, "name") ?? bucketId;
            var fraction = ReadFraction(bucket);
            var disabled = bucket.TryGetProperty("disabled", out var flag)
                && flag.ValueKind == JsonValueKind.True;
            var reset = ParseDate(ReadString(bucket, "resetTime"));
            var cadence = ClassifyCadence(bucket, bucketId, bucketName);

            windows.Add(new QuotaWindow(
                groupName,
                bucketId,
                bucketName,
                cadence,
                fraction,
                reset,
                !disabled && fraction is not null));
        }
    }

    private static UsageSnapshot BuildSnapshot(List<QuotaWindow> windows, DateTimeOffset now)
    {
        var ranked = windows
            .Where(static window => window.UsageKnown)
            .OrderByDescending(static window => window.UsedPercent)
            .ToList();

        var gemini = ranked.FirstOrDefault(static window => window.Family == "Gemini");
        var claudeGpt = ranked.FirstOrDefault(static window => window.Family == "Claude/GPT");
        var primaryWindow = gemini ?? ranked[0];
        var secondaryWindow = claudeGpt is not null && !ReferenceEquals(claudeGpt, primaryWindow)
            ? claudeGpt
            : null;

        var extras = new List<NamedRateWindow>();
        foreach (var window in windows)
        {
            if (ReferenceEquals(window, primaryWindow) || ReferenceEquals(window, secondaryWindow))
            {
                continue;
            }

            extras.Add(new NamedRateWindow(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"antigravity-{Slug(window.Family)}-{Slug(window.BucketId)}"),
                window.Title,
                window.ToRateWindow(),
                window.UsageKnown));
        }

        return new UsageSnapshot(
            ProviderKeys.Antigravity,
            now,
            primaryWindow.ToRateWindow(),
            secondaryWindow?.ToRateWindow(),
            extras);
    }

    private static string Slug(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(characters).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "quota" : slug;
    }

    private static string ClassifyCadence(JsonElement bucket, string bucketId, string bucketName)
    {
        var explicitWindow = ReadString(bucket, "window")?.Trim().ToLowerInvariant().Replace('_', '-');
        var candidates = string.IsNullOrEmpty(explicitWindow)
            ? new[] { bucketId.ToLowerInvariant().Replace('_', '-'), bucketName.ToLowerInvariant().Replace('_', '-') }
            : [explicitWindow];

        foreach (var candidate in candidates)
        {
            var value = candidate.EndsWith(" limit", StringComparison.Ordinal)
                ? candidate[..^" limit".Length]
                : candidate;
            if (value is "session" or "5h" or "5-hour" or "five hour" or "five-hour"
                || value.EndsWith("-session", StringComparison.Ordinal)
                || value.EndsWith("-5h", StringComparison.Ordinal))
            {
                return "Session";
            }

            if (value == "weekly" || value.EndsWith("-weekly", StringComparison.Ordinal))
            {
                return "Weekly";
            }
        }

        return "Other";
    }

    private static double? ReadFraction(JsonElement bucket)
    {
        if (bucket.TryGetProperty("remainingFraction", out var direct)
            && direct.ValueKind == JsonValueKind.Number)
        {
            return direct.GetDouble();
        }

        if (!bucket.TryGetProperty("remaining", out var remaining) || remaining.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (remaining.TryGetProperty("remainingFraction", out var nested) && nested.ValueKind == JsonValueKind.Number)
        {
            return nested.GetDouble();
        }

        return remaining.TryGetProperty("case", out var oneOf)
            && oneOf.ValueKind == JsonValueKind.String
            && oneOf.GetString() == "remainingFraction"
            && remaining.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : null;
    }

    private static bool IsOkCode(JsonElement code) =>
        code.ValueKind switch
        {
            JsonValueKind.Number => code.GetInt32() == 0,
            JsonValueKind.String => code.GetString()?.ToLowerInvariant() is "ok" or "success" or "0",
            _ => true,
        };

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

    private static ProviderError ParseError(Exception? cause = null) =>
        new(ProviderErrorKind.Parse, "Could not read the Antigravity quota response. Will retry shortly.", cause: cause);

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);

    private sealed record QuotaWindow(
        string GroupName,
        string BucketId,
        string BucketName,
        string Cadence,
        double? RemainingFraction,
        DateTimeOffset? ResetsAt,
        bool UsageKnown)
    {
        public string Family => this.GroupName.Contains("gemini", StringComparison.OrdinalIgnoreCase)
            && !this.GroupName.Any(char.IsDigit)
                ? "Gemini"
                : this.GroupName.Contains("claude", StringComparison.OrdinalIgnoreCase)
                    || this.GroupName.Contains("gpt", StringComparison.OrdinalIgnoreCase)
                        ? "Claude/GPT"
                        : this.GroupName;

        public string Title => $"{this.Family} {this.Cadence switch
        {
            "Session" => "5-hour",
            "Weekly" => "weekly",
            _ => this.BucketName,
        }}";

        public double UsedPercent => Math.Clamp((1d - (this.RemainingFraction ?? 1d)) * 100d, 0d, 200d);

        public RateWindow ToRateWindow()
        {
            var window = this.Cadence switch
            {
                "Session" => TimeSpan.FromMinutes(300),
                "Weekly" => TimeSpan.FromMinutes(10080),
                _ => (TimeSpan?)null,
            };

            return new RateWindow(this.UsedPercent, window, this.ResetsAt);
        }
    }
}
