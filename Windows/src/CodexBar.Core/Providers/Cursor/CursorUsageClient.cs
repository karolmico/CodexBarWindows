using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Cursor;

/// <summary>
/// Fetches Cursor's usage summary with the stored session cookie and maps the plan, included-request,
/// auto, and API lanes. Legacy request-based plans fall back to <c>/api/usage</c>.
/// </summary>
public sealed class CursorUsageClient
{
    public static readonly Uri DefaultBaseUri = new("https://cursor.com");

    private const string CookieName = "WorkosCursorSessionToken";

    private static readonly Dictionary<string, string> MembershipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enterprise"] = "Enterprise",
        ["express"] = "Start",
        ["free"] = "Free",
        ["free_trial"] = "Pro Trial",
        ["hobby"] = "Hobby",
        ["pro"] = "Pro",
        ["pro_student"] = "Pro",
        ["pro_plus"] = "Pro+",
        ["team"] = "Team",
        ["ultra"] = "Ultra",
    };

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly Uri baseUri;

    public CursorUsageClient(HttpClient httpClient, Func<DateTimeOffset>? clock = null, Uri? baseUri = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.baseUri = baseUri ?? DefaultBaseUri;
    }

    public async Task<UsageFetchResult> FetchAsync(CursorSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var summary = await this.GetAsync("/api/usage-summary", session, cancellationToken).ConfigureAwait(false);
        if (summary.Error is not null)
        {
            return summary.Error;
        }

        var profile = await this.TryGetProfileAsync(session, cancellationToken).ConfigureAwait(false);

        JsonDocument? usage = null;
        try
        {
            var summaryRoot = summary.Document!.RootElement;
            if (summaryRoot.ValueKind != JsonValueKind.Object)
            {
                return ParseFailure();
            }

            var plan = PlanBlock(summaryRoot);
            var planPercent = ReadPlanPercent(plan);
            int? requestsUsed = null;
            int? requestsLimit = null;

            if (planPercent is null)
            {
                // Legacy request-based plans have no plan block; their quota lives in /api/usage.
                var legacy = await this.TryGetLegacyUsageAsync(session, cancellationToken).ConfigureAwait(false);
                usage = legacy;
                if (legacy is not null)
                {
                    (requestsUsed, requestsLimit) = ReadLegacyRequests(legacy.RootElement);
                }
            }

            return new UsageFetchResult.Success(
                this.BuildSnapshot(session, summaryRoot, profile, planPercent, requestsUsed, requestsLimit));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            return ParseFailure(exception);
        }
        finally
        {
            usage?.Dispose();
            summary.Document?.Dispose();
        }
    }

    private UsageSnapshot BuildSnapshot(
        CursorSession session,
        JsonElement root,
        JsonElement? profile,
        double? planPercent,
        int? requestsUsed,
        int? requestsLimit)
    {
        var start = ReadDate(root, "billingCycleStart");
        var end = ReadDate(root, "billingCycleEnd");
        var window = start is { } from && end is { } to && to > from ? to - from : (TimeSpan?)null;
        var membership = ReadString(root, "membershipType");
        var plan = PlanBlock(root);
        var autoPercent = ReadDouble(plan, "autoPercentUsed");
        var apiPercent = ReadDouble(plan, "apiPercentUsed");

        RateWindow? primary;
        if (requestsUsed is { } used && requestsLimit is { } limit and > 0)
        {
            primary = new RateWindow(
                Math.Clamp(used / (double)limit * 100d, 0d, 200d),
                window,
                end,
                quotaLimit: limit,
                quotaUnit: "requests");
        }
        else if (planPercent is { } percent)
        {
            primary = new RateWindow(percent, window, end);
        }
        else
        {
            primary = null;
        }

        var secondary = primary is not null && planPercent is not null && autoPercent is { } auto
            ? new RateWindow(auto, window, end)
            : null;
        var extras = new List<NamedRateWindow>();
        if (secondary is not null && apiPercent is { } api)
        {
            extras.Add(new NamedRateWindow("api", "API", new RateWindow(api, window, end)));
        }

        var email = profile is { } me ? ReadString(me, "email") : session.Email;
        var identity = new ProviderIdentity(
            membership is null ? null : FormatMembership(membership),
            email);

        return new UsageSnapshot(ProviderKeys.Cursor, this.clock(), primary, secondary, extras, identity);
    }

    private async Task<JsonElement?> TryGetProfileAsync(CursorSession session, CancellationToken cancellationToken)
    {
        var profile = await this.GetAsync("/api/auth/me", session, cancellationToken).ConfigureAwait(false);
        try
        {
            if (profile.Error is not null || profile.Document is null)
            {
                return null;
            }

            return profile.Document.RootElement.Clone();
        }
        finally
        {
            profile.Document?.Dispose();
        }
    }

    private async Task<JsonDocument?> TryGetLegacyUsageAsync(
        CursorSession session,
        CancellationToken cancellationToken)
    {
        var usage = await this.GetAsync(
            $"/api/usage?user={Uri.EscapeDataString(session.UserId)}",
            session,
            cancellationToken).ConfigureAwait(false);
        return usage.Error is null ? usage.Document : null;
    }

    private async Task<(JsonDocument? Document, UsageFetchResult.Failure? Error)> GetAsync(
        string path,
        CursorSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(this.baseUri, path));
        request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={session.CookieValue}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "CodexBar/0.1 (Windows)");

        HttpResponseMessage response;
        try
        {
            response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return (null, Failure(new ProviderError(
                ProviderErrorKind.Network,
                "Could not reach the Cursor usage service. Will retry shortly.",
                cause: exception)));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return (null, Failure(ProviderError.AuthenticationRequired(
                    "Cursor rejected the stored session. Sign in to the Cursor app again.")));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return (null, Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "Cursor rate-limited the usage request. Will retry shortly.",
                    RetryAfter(response))));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return (null, Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Cursor usage request failed (HTTP {(int)response.StatusCode})."))));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return (JsonDocument.Parse(body), null);
            }
            catch (JsonException exception)
            {
                return (null, Failure(new ProviderError(
                    ProviderErrorKind.Parse,
                    "Could not read the Cursor usage response. Will retry shortly.",
                    cause: exception)));
            }
        }
    }

    private static JsonElement? PlanBlock(JsonElement root) =>
        root.TryGetProperty("individualUsage", out var individual)
        && individual.ValueKind == JsonValueKind.Object
        && individual.TryGetProperty("plan", out var plan)
        && plan.ValueKind == JsonValueKind.Object
            ? plan
            : null;

    private static double? ReadPlanPercent(JsonElement? plan)
    {
        if (plan is not { } block)
        {
            return null;
        }

        var total = ReadDouble(block, "totalPercentUsed");
        if (total is { } value)
        {
            return value;
        }

        var auto = ReadDouble(block, "autoPercentUsed");
        var api = ReadDouble(block, "apiPercentUsed");
        if (auto is { } autoValue && api is { } apiValue)
        {
            return (autoValue + apiValue) / 2d;
        }

        if (auto is { } autoOnly)
        {
            return autoOnly;
        }

        if (api is { } apiOnly)
        {
            return apiOnly;
        }

        var used = ReadDouble(block, "used");
        var limit = ReadDouble(block, "limit");
        return used is { } usedValue && limit is > 0 ? usedValue / limit.Value * 100d : null;
    }

    private static (int? Used, int? Limit) ReadLegacyRequests(JsonElement root)
    {
        if (!root.TryGetProperty("gpt-4", out var gpt) || gpt.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var limit = ReadInt(gpt, "maxRequestUsage");
        var used = ReadInt(gpt, "numRequestsTotal") ?? ReadInt(gpt, "numRequests");
        return (used, limit);
    }

    private static string FormatMembership(string value)
    {
        var key = value.Trim();
        return MembershipNames.TryGetValue(key, out var display) ? $"Cursor {display}" : value;
    }

    private static double? ReadDouble(JsonElement? parent, string propertyName)
    {
        if (parent is not { } element
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var number = value.GetDouble();
        return double.IsFinite(number) ? number : null;
    }

    private static int? ReadInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var number) ? number : (int?)null;
    }

    private static string? ReadString(JsonElement? parent, string propertyName) =>
        parent is { } element
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName) =>
        ReadString(root, propertyName) is { } text
        && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        return retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
    }

    private static UsageFetchResult.Failure ParseFailure(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the Cursor usage response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult.Failure Failure(ProviderError error) => new(error);
}
