using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Copilot;

/// <summary>
/// Finds a GitHub token for the Copilot API: explicit environment variables first, then the signed-in
/// GitHub CLI. The token is only ever placed in an Authorization header.
/// </summary>
public sealed class CopilotTokenSource
{
    private readonly Func<IReadOnlyDictionary<string, string>> environment;
    private readonly Func<CancellationToken, Task<string?>> runGitHubCli;

    public CopilotTokenSource(
        Func<IReadOnlyDictionary<string, string>>? environment = null,
        Func<CancellationToken, Task<string?>>? runGitHubCli = null)
    {
        this.environment = environment ?? ReadEnvironment;
        this.runGitHubCli = runGitHubCli ?? RunGitHubCliAsync;
    }

    public async Task<string?> ResolveAsync(CancellationToken cancellationToken)
    {
        var environment = this.environment();
        foreach (var key in new[] { "GITHUB_TOKEN", "GH_TOKEN" })
        {
            if (environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var cliToken = await this.runGitHubCli(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(cliToken) ? null : cliToken.Trim();
    }

    private static async Task<string?> RunGitHubCliAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "GITHUB_TOKEN", "GH_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return values;
    }
}

/// <summary>Fetches Copilot premium-request usage from the GitHub Copilot endpoint.</summary>
public sealed class CopilotUsageClient
{
    public static readonly Uri DefaultEndpoint = new("https://api.github.com/copilot_internal/user");

    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> clock;
    private readonly Uri endpoint;

    public CopilotUsageClient(HttpClient httpClient, Func<DateTimeOffset>? clock = null, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<UsageFetchResult> FetchAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A Copilot token is required.", nameof(token));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, this.endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"token {token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
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
                "Could not reach the GitHub Copilot service. Will retry shortly.",
                cause: exception));
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return Failure(ProviderError.AuthenticationRequired(
                    "GitHub rejected the stored token. Run `gh auth login` and try again."));
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.RateLimited,
                    "GitHub rate-limited the Copilot request. Will retry shortly.",
                    RetryAfter(response)));
            }

            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode >= 500 ? ProviderErrorKind.Network : ProviderErrorKind.Unknown;
                return Failure(new ProviderError(
                    kind,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"GitHub Copilot request failed (HTTP {(int)response.StatusCode}).")));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(body, this.clock());
        }
    }

    private static UsageFetchResult Parse(string body, DateTimeOffset now)
    {
        try
        {
            var root = JsonNode.Parse(body) as JsonObject;
            if (root is null)
            {
                return ParseFailure();
            }

            var plan = root["copilot_plan"]?.GetValue<string>();
            var resetDate = root["quota_reset_date"]?.GetValue<string>() is { } text
                && DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed)
                    ? parsed
                    : (DateTimeOffset?)null;
            var tokenBasedBilling = root["token_based_billing"]?.GetValue<bool>() ?? false;

            var snapshots = root["quota_snapshots"] as JsonObject;
            var premium = ReadQuota(snapshots?["premium_interactions"] as JsonObject, resetDate);
            var chat = ReadQuota(snapshots?["chat"] as JsonObject, resetDate);

            var identity = plan is null ? null : new ProviderIdentity(planName: TitleCase(plan));
            if (tokenBasedBilling || premium is null)
            {
                // Token-based billing or an unlimited seat carries no metered percentage lane.
                return new UsageFetchResult.Success(new UsageSnapshot(
                    ProviderKeys.Copilot,
                    now,
                    identity: identity));
            }

            return new UsageFetchResult.Success(new UsageSnapshot(
                ProviderKeys.Copilot,
                now,
                premium,
                chat,
                identity: identity));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            return ParseFailure(exception);
        }
    }

    private static RateWindow? ReadQuota(JsonObject? quota, DateTimeOffset? resetsAt)
    {
        if (quota is null || quota["unlimited"]?.GetValue<bool>() == true)
        {
            return null;
        }

        var entitlement = ReadNumber(quota["entitlement"]);
        var remaining = ReadNumber(quota["remaining"]);
        var percentRemaining = ReadNumber(quota["percent_remaining"]);

        // A zero-entitlement placeholder carries no quota signal; never render it as 0% used.
        if (entitlement == 0 && remaining == 0 && (percentRemaining is null || percentRemaining == 100))
        {
            return null;
        }

        var percentUsed = percentRemaining is { } percent
            ? 100d - percent
            : entitlement is > 0 && remaining is { } left
                ? 100d - (left / entitlement.Value * 100d)
                : (double?)null;

        return percentUsed is null ? null : new RateWindow(Math.Clamp(percentUsed.Value, 0d, 200d), resetsAt: resetsAt);
    }

    private static double? ReadNumber(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.Number => node.GetValue<double>(),
                JsonValueKind.String when double.TryParse(
                    node.GetValue<string>(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) => parsed,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant());

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        return retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
    }

    private static UsageFetchResult ParseFailure(Exception? cause = null) =>
        Failure(new ProviderError(
            ProviderErrorKind.Parse,
            "Could not read the Copilot usage response. Will retry shortly.",
            cause: cause));

    private static UsageFetchResult Failure(ProviderError error) => new UsageFetchResult.Failure(error);
}
