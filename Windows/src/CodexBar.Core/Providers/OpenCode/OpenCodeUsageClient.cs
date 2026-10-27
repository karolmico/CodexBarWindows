using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.OpenCode;

/// <summary>
/// Reads the OpenCode Go subscription meters from the public usage API. The endpoint reports the
/// rolling 5-hour, weekly and monthly percentages in whole percent units, plus absolute reset
/// instants. Any failure is a value, never an exception, so the tray and CLI keep rendering.
/// </summary>
public sealed class OpenCodeUsageClient
{
    public static readonly Uri UsageEndpoint = new("https://opencode.ai/zen/go/v1/usage");

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan attemptTimeout;

    public OpenCodeUsageClient(
        HttpClient httpClient,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? attemptTimeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<UsageFetchResult> FetchAsync(
        OpenCodeCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (credential.IsExpired(this.clock()))
        {
            return Failure(ProviderError.AuthenticationRequired(
                "The OpenCode sign-in has expired. Run `opencode auth login` and try again."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "CodexBar");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(this.attemptTimeout);

        HttpResponseMessage response;
        try
        {
            response = await this.httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "OpenCode did not answer in time. Will retry shortly.",
                cause: exception));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "Could not reach OpenCode. Will retry shortly.",
                cause: exception));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(ProviderError.AuthenticationRequired(
                    "OpenCode rejected the credential. Run `opencode auth login`, or replace the API key in provider setup."));
            }

            if ((int)response.StatusCode == 429)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "OpenCode is rate limiting usage requests. Will retry shortly.",
                    RetryAfter(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"OpenCode returned HTTP {(int)response.StatusCode}.")));
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ParseUsage(body, this.clock());
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the documented Go usage document. The rolling window is required; weekly and monthly
    /// are optional, so an account without a weekly meter still gets a snapshot.
    /// </summary>
    public static UsageFetchResult ParseUsage(string body, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return ParseError();
            }

            if (!TryReadWindow(usage, "rolling", now, out var rolling) || rolling is null)
            {
                return ParseError();
            }

            var weekly = TryReadWindow(usage, "weekly", now, out var weeklyWindow) ? weeklyWindow : null;
            var monthly = TryReadWindow(usage, "monthly", now, out var monthlyWindow) ? monthlyWindow : null;

            var extras = new List<NamedRateWindow>();
            if (monthly is not null)
            {
                extras.Add(new NamedRateWindow(
                    "opencode-monthly",
                    "OpenCode monthly",
                    monthly.ToRateWindow(TimeSpan.FromDays(30))));
            }

            return new UsageFetchResult.Success(new UsageSnapshot(
                ProviderKeys.OpenCode,
                now,
                rolling.ToRateWindow(TimeSpan.FromHours(5)),
                weekly?.ToRateWindow(TimeSpan.FromDays(7)),
                extras));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentOutOfRangeException)
        {
            return ParseError(exception);
        }
    }

    private static bool TryReadWindow(
        JsonElement usage,
        string name,
        DateTimeOffset now,
        out OpenCodeUsageWindow? window)
    {
        window = null;
        if (!usage.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (ReadString(element, "status") is { } status
            && !string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var percent = ReadNumber(element, "percent", "usagePercent", "usage_percent");
        if (percent is null)
        {
            return false;
        }

        window = new OpenCodeUsageWindow(
            Math.Clamp(percent.Value, 0d, 100d),
            ReadReset(element, now));
        return true;
    }

    private static DateTimeOffset? ReadReset(JsonElement element, DateTimeOffset now)
    {
        foreach (var name in new[] { "resetsAt", "resetAt", "resetTime", "resets_at", "reset_time" })
        {
            if (ReadString(element, name) is not { Length: > 0 } text)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                return parsed;
            }
        }

        foreach (var name in new[] { "resetInSec", "resetInSeconds", "reset_in_sec" })
        {
            if (ReadNumber(element, name) is { } seconds && seconds >= 0)
            {
                return now.AddSeconds(seconds);
            }
        }

        return null;
    }

    private static double? ReadNumber(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number)
                && double.IsFinite(number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static UsageFetchResult ParseError(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the OpenCode usage response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);

    private sealed record OpenCodeUsageWindow(double UsedPercent, DateTimeOffset? ResetsAt)
    {
        public RateWindow ToRateWindow(TimeSpan window) => new(this.UsedPercent, window, this.ResetsAt);
    }
}
