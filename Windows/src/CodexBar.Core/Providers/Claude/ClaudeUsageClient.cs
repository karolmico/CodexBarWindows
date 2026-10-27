using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Claude;

/// <summary>
/// Calls the Claude OAuth usage endpoint and maps its session, weekly, and scoped windows. Scoped windows
/// without a utilization value are omitted instead of rendered as zero usage.
/// </summary>
public sealed class ClaudeUsageClient
{
    public static readonly Uri DefaultEndpoint = new("https://api.anthropic.com/api/oauth/usage?cedar_ember=1");

    private const string BetaHeader = "oauth-2025-04-20";
    private const string UserAgent = "claude-cli/2.1.0 (external, cli)";

    private static readonly (string Property, string Id, string Title)[] ScopedWindows =
    [
        ("seven_day_opus", "opus", "Opus"),
        ("seven_day_sonnet", "sonnet", "Sonnet"),
        ("seven_day_oauth_apps", "oauth-apps", "OAuth apps"),
        ("seven_day_routines", "routines", "Daily Routines"),
        ("seven_day_claude_routines", "claude-routines", "Claude Routines"),
        ("seven_day_cowork", "cowork", "Cowork"),
    ];

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly Uri endpoint;

    public ClaudeUsageClient(HttpClient httpClient, Func<DateTimeOffset>? clock = null, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<UsageFetchResult> FetchAsync(
        ClaudeCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using var request = new HttpRequestMessage(HttpMethod.Get, this.endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

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
                "Could not reach the Claude usage service. Will retry shortly.",
                cause: exception));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(ProviderError.AuthenticationRequired(
                    "Claude rejected the stored token. Run `claude` to sign in again."));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "Claude rate-limited the usage request. Will retry shortly.",
                    RetryAfter(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Claude usage request failed (HTTP {(int)response.StatusCode}).")));
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

        return retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
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

            var primary = MapWindow(GetProperty(root, "five_hour"), TimeSpan.FromHours(5));
            var secondary = MapWindow(GetProperty(root, "seven_day"), TimeSpan.FromDays(7));

            var extras = new List<NamedRateWindow>();
            foreach (var (property, id, title) in ScopedWindows)
            {
                if (MapWindow(GetProperty(root, property), TimeSpan.FromDays(7)) is { } window)
                {
                    extras.Add(new NamedRateWindow(id, title, window));
                }
            }

            return new UsageFetchResult.Success(new UsageSnapshot(
                ProviderKeys.Claude,
                now,
                primary,
                secondary,
                extras));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ParseFailure(exception);
        }
    }

    private static RateWindow? MapWindow(JsonElement? element, TimeSpan window)
    {
        if (element is not { ValueKind: JsonValueKind.Object } rateWindow)
        {
            return null;
        }

        // A scoped window may expose only reset metadata; that is "no measurement", not 0% used.
        if (!rateWindow.TryGetProperty("utilization", out var utilization)
            || utilization.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (utilization.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException("'utilization' must be a number.");
        }

        var resetsAt = rateWindow.TryGetProperty("resets_at", out var reset)
            && reset.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                reset.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsedReset)
                ? parsedReset
                : (DateTimeOffset?)null;

        return new RateWindow(utilization.GetDouble(), window, resetsAt);
    }

    private static JsonElement? GetProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.Object
            ? value
            : null;

    private static UsageFetchResult ParseFailure(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the Claude usage response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);
}
