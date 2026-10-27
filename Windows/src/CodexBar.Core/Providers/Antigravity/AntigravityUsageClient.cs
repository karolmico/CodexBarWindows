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

            var lanes = new List<AntigravityQuotaLane>();
            foreach (var group in groups.EnumerateArray())
            {
                ReadGroup(group, lanes);
            }

            if (lanes.Count == 0 || !lanes.Any(static lane => lane.UsageKnown))
            {
                return (null, new ProviderError(
                    ProviderErrorKind.Parse,
                    "Antigravity reported no usable quota buckets. Will retry shortly."));
            }

            return (AntigravityQuotaSnapshot.Build(lanes, now), null);
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

    private static void ReadGroup(JsonElement group, List<AntigravityQuotaLane> lanes)
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
            var cadence = AntigravityQuotaSnapshot.ClassifyCadence(
                ReadString(bucket, "window"),
                bucketId,
                bucketName);

            lanes.Add(new AntigravityQuotaLane(
                groupName,
                bucketId,
                bucketName,
                cadence,
                fraction,
                reset,
                !disabled && fraction is not null,
                disabled,
                ReadString(bucket, "description")));
        }
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
}
