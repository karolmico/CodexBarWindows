using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Codex;

/// <summary>
/// Calls the ChatGPT backend usage endpoint with the CLI's access token and maps the payload into the core
/// snapshot. Every expected failure becomes a <see cref="ProviderError"/>; cancellation still propagates.
/// </summary>
public sealed class CodexUsageClient
{
    public static readonly Uri DefaultEndpoint = new("https://chatgpt.com/backend-api/wham/usage");

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly Uri endpoint;

    public CodexUsageClient(HttpClient httpClient, Func<DateTimeOffset>? clock = null, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<UsageFetchResult> FetchAsync(
        CodexCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using var request = new HttpRequestMessage(HttpMethod.Get, this.endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.UserAgent.ParseAdd("CodexBar/0.1 (+https://github.com/karolmico/CodexBarWindows)");
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }

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
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "Could not reach the Codex usage service. Will retry shortly.",
                cause: exception));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(ProviderError.AuthenticationRequired(
                    "Codex rejected the stored token. Run `codex login` to sign in again."));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "Codex rate-limited the usage request. Will retry shortly.",
                    RetryAfter(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Codex usage request failed (HTTP {(int)response.StatusCode}).")));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(body, this.clock());
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return null;
    }

    private static UsageFetchResult Parse(string body, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseFailure();
            }

            var plan = PlanName(TryGetString(root, "plan_type"));
            var rateLimit = root.TryGetProperty("rate_limit", out var limits) && limits.ValueKind == JsonValueKind.Object
                ? limits
                : (JsonElement?)null;

            var primary = rateLimit is { } primaryLimits
                ? MapWindow(GetProperty(primaryLimits, "primary_window"))
                : null;
            var secondary = rateLimit is { } secondaryLimits
                ? MapWindow(GetProperty(secondaryLimits, "secondary_window"))
                : null;

            var snapshot = new UsageSnapshot(
                ProviderKeys.Codex,
                now,
                primary,
                secondary,
                identity: plan is null ? null : new ProviderIdentity(planName: plan));

            return new UsageFetchResult.Success(snapshot);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ParseFailure(exception);
        }
    }

    /// <summary>
    /// Returns the window when present, <c>null</c> when the provider omitted it, and throws when it is
    /// present but malformed so the caller can report an unreadable response instead of a silent zero.
    /// </summary>
    private static RateWindow? MapWindow(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } window)
        {
            return null;
        }

        var usedPercent = ReadRequiredNumber(window, "used_percent");
        var limitSeconds = ReadOptionalNumber(window, "limit_window_seconds");
        var resetAt = ReadOptionalNumber(window, "reset_at");

        return new RateWindow(
            usedPercent,
            window: limitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            resetsAt: resetAt is { } unixSeconds ? DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds) : null);
    }

    private static double ReadRequiredNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException($"'{propertyName}' must be a number.");
        }

        return value.GetDouble();
    }

    private static double? ReadOptionalNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : throw new FormatException($"'{propertyName}' must be a number when present.");
    }

    private static JsonElement? GetProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Maps upstream plan identifiers to the label the tray shows.</summary>
    private static string? PlanName(string? rawPlan) => rawPlan?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "plus" => "Plus",
        "pro" => "Pro",
        "free" => "Free",
        "go" => "Go",
        "team" => "Team",
        "business" => "Business",
        "enterprise" => "Enterprise",
        "education" => "Education",
        "edu" => "Edu",
        "free_workspace" => "Free workspace",
        var other => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(other.Replace('_', ' ')),
    };

    private static UsageFetchResult ParseFailure(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the Codex usage response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);
}
