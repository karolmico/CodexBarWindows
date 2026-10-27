using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Gemini;

/// <summary>
/// Resolves the Gemini Code Assist project, then fetches per-model quota buckets and groups them into
/// Pro (primary), Flash (secondary), and Flash Lite (named window) lanes.
/// </summary>
public sealed class GeminiUsageClient
{
    public static readonly Uri DefaultCodeAssistEndpoint =
        new("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist");

    public static readonly Uri DefaultQuotaEndpoint =
        new("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota");

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly Uri codeAssistEndpoint;
    private readonly Uri quotaEndpoint;

    public GeminiUsageClient(
        HttpClient httpClient,
        Func<DateTimeOffset>? clock = null,
        Uri? codeAssistEndpoint = null,
        Uri? quotaEndpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.codeAssistEndpoint = codeAssistEndpoint ?? DefaultCodeAssistEndpoint;
        this.quotaEndpoint = quotaEndpoint ?? DefaultQuotaEndpoint;
    }

    public async Task<UsageFetchResult> FetchAsync(
        GeminiCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var status = await this.LoadCodeAssistAsync(credentials.AccessToken, cancellationToken).ConfigureAwait(false);

        var body = status.ProjectId is { } projectId
            ? JsonSerializer.Serialize(new { project = projectId })
            : "{}";

        using var request = new HttpRequestMessage(HttpMethod.Post, this.quotaEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
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
            return Failure(new ProviderError(
                ProviderErrorKind.Network,
                "Could not reach the Gemini quota service. Will retry shortly.",
                cause: exception));
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(ProviderError.AuthenticationRequired(
                    "Gemini rejected the stored token. Run `gemini` to sign in again."));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "Gemini rate-limited the quota request. Will retry shortly.",
                    RetryAfter(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Gemini quota request failed (HTTP {(int)response.StatusCode}).")));
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(payload, status, credentials.IdToken, this.clock());
        }
    }

    private async Task<CodeAssistStatus> LoadCodeAssistAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, this.codeAssistEndpoint)
        {
            Content = new StringContent(
                "{\"metadata\":{\"ideType\":\"GEMINI_CLI\",\"pluginType\":\"GEMINI\"}}",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return CodeAssistStatus.Empty;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var project = ReadProject(root);
            var plan = ReadNestedString(root, "paidTier", "name")
                ?? ReadNestedString(root, "currentTier", "name")
                ?? ReadNestedString(root, "currentTier", "id");

            return new CodeAssistStatus(project, plan);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Project/plan discovery is best-effort: quota can still be fetched without them.
            return CodeAssistStatus.Empty;
        }
    }

    private static UsageFetchResult Parse(
        string body,
        CodeAssistStatus status,
        string? idToken,
        DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("buckets", out var buckets)
                || buckets.ValueKind != JsonValueKind.Array)
            {
                return ParseFailure();
            }

            var lowestByModel = new Dictionary<string, (double Fraction, DateTimeOffset? ResetsAt)>(StringComparer.OrdinalIgnoreCase);
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object
                    || ReadString(bucket, "modelId") is not { } modelId
                    || !TryReadFraction(bucket, out var fraction))
                {
                    continue;
                }

                if (!lowestByModel.TryGetValue(modelId, out var existing) || fraction < existing.Fraction)
                {
                    lowestByModel[modelId] = (fraction, ReadReset(bucket));
                }
            }

            if (lowestByModel.Count == 0)
            {
                // A valid but empty quota payload means "nothing reported", not "0% used".
                return new UsageFetchResult.Success(new UsageSnapshot(
                    ProviderKeys.Gemini,
                    now,
                    identity: new ProviderIdentity(planName: status.Plan, accountLabel: Email(idToken))));
            }

            var pro = Lowest(lowestByModel, static key => key.Contains("pro", StringComparison.OrdinalIgnoreCase));
            var flashLite = Lowest(
                lowestByModel,
                static key => key.Contains("flash-lite", StringComparison.OrdinalIgnoreCase));
            var flash = Lowest(
                lowestByModel,
                static key => key.Contains("flash", StringComparison.OrdinalIgnoreCase)
                    && !key.Contains("flash-lite", StringComparison.OrdinalIgnoreCase));

            var extras = new List<NamedRateWindow>();
            if (flashLite is { } flashLiteWindow)
            {
                extras.Add(new NamedRateWindow("flash-lite", "Flash Lite", ToWindow(flashLiteWindow)));
            }

            var identity = new ProviderIdentity(planName: status.Plan, accountLabel: Email(idToken));
            return new UsageFetchResult.Success(new UsageSnapshot(
                ProviderKeys.Gemini,
                now,
                pro is { } proWindow ? ToWindow(proWindow) : null,
                flash is { } flashWindow ? ToWindow(flashWindow) : null,
                extras,
                identity));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ParseFailure(exception);
        }
    }

    private static RateWindow ToWindow((double Fraction, DateTimeOffset? ResetsAt) value) =>
        new(Math.Clamp((1d - value.Fraction) * 100d, 0d, 200d), TimeSpan.FromMinutes(1440), value.ResetsAt);

    /// <summary>Lowest remaining fraction among models matching the predicate; null when none match.</summary>
    private static (double Fraction, DateTimeOffset? ResetsAt)? Lowest(
        Dictionary<string, (double Fraction, DateTimeOffset? ResetsAt)> byModel,
        Func<string, bool> predicate)
    {
        (double Fraction, DateTimeOffset? ResetsAt)? lowest = null;
        foreach (var (model, value) in byModel)
        {
            if (!predicate(model))
            {
                continue;
            }

            if (lowest is null || value.Fraction < lowest.Value.Fraction)
            {
                lowest = value;
            }
        }

        return lowest;
    }

    private static bool TryReadFraction(JsonElement bucket, out double fraction)
    {
        fraction = 0d;
        if (!bucket.TryGetProperty("remainingFraction", out var remaining)
            || remaining.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        fraction = remaining.GetDouble();
        return double.IsFinite(fraction);
    }

    private static DateTimeOffset? ReadReset(JsonElement bucket) =>
        ReadString(bucket, "resetTime") is { } text
        && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string? ReadProject(JsonElement root)
    {
        if (!root.TryGetProperty("cloudaicompanionProject", out var project))
        {
            return null;
        }

        return project.ValueKind switch
        {
            JsonValueKind.String => project.GetString(),
            JsonValueKind.Object when project.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String =>
                id.GetString(),
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static string? ReadNestedString(JsonElement element, string nested, string propertyName) =>
        element.TryGetProperty(nested, out var child) ? ReadString(child, propertyName) : null;

    /// <summary>Reads the account email from the id token payload. The token is never logged.</summary>
    private static string? Email(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("email", out var email)
                && email.ValueKind == JsonValueKind.String
                    ? email.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        return retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
    }

    private static UsageFetchResult ParseFailure(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the Gemini quota response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);

    private sealed record CodeAssistStatus(string? ProjectId, string? Plan)
    {
        public static CodeAssistStatus Empty { get; } = new(null, null);
    }
}
