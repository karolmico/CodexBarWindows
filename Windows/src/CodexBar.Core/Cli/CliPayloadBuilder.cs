using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Cli;

/// <summary>
/// JSON payloads shaped like the macOS CLI's <c>usage --format json</c> output. Only fields this port
/// can produce are emitted; everything else is omitted instead of being faked.
/// </summary>
public sealed record CliProviderPayload(
    string Provider,
    string? Account,
    string? Version,
    string Source,
    CliUsagePayload? Usage,
    CliWindowLabelsPayload? RateWindowLabels,
    CliErrorPayload? Error);

public sealed record CliUsagePayload(
    CliRateWindowPayload? Primary,
    CliRateWindowPayload? Secondary,
    IReadOnlyList<CliNamedWindowPayload>? ExtraRateWindows,
    string UpdatedAt,
    CliIdentityPayload? Identity);

public sealed record CliRateWindowPayload(
    double UsedPercent,
    int? WindowMinutes,
    string? ResetsAt,
    string? ResetDescription,
    string? Title,
    double? QuotaLimit,
    string? QuotaUnit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool IsSyntheticPlaceholder);

public sealed record CliNamedWindowPayload(
    string Id,
    string Title,
    CliRateWindowPayload Window,
    bool? UsageKnown);

public sealed record CliWindowLabelsPayload(string? Primary, string? Secondary, string? Tertiary);

public sealed record CliIdentityPayload(string? Account, string? Organization, string? Plan);

public sealed record CliErrorPayload(int Code, string Message, string Kind, string ProviderErrorKind);

public static class CliPayloadBuilder
{
    public static IReadOnlyList<CliProviderPayload> Build(
        IReadOnlyList<CliProviderResult> results,
        string? version = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        return results
            .Select(result => new CliProviderPayload(
                result.Key.Value,
                result.Snapshot?.Identity?.AccountLabel,
                version,
                result.Source,
                result.Snapshot is { } snapshot ? BuildUsage(snapshot) : null,
                result.Snapshot is { } labeled ? BuildLabels(labeled) : null,
                result.Error is { } error
                    ? new CliErrorPayload(
                        1,
                        error.Message,
                        "provider",
                        CamelCase(error.Kind.ToString()))
                    : null))
            .ToArray();
    }

    public static string ToJson(IReadOnlyList<CliProviderPayload> payload, bool pretty)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = pretty,
        };

        return JsonSerializer.Serialize(payload, options);
    }

    /// <summary>UTC instant with whole seconds, matching Swift's <c>JSONEncoder.DateEncodingStrategy.iso8601</c>.</summary>
    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static CliUsagePayload BuildUsage(UsageSnapshot snapshot)
    {
        var extras = snapshot.ExtraWindows.Count == 0
            ? null
            : snapshot.ExtraWindows
                .Select(extra => new CliNamedWindowPayload(
                    extra.Id,
                    extra.Title,
                    BuildWindow(extra.Window),
                    UsageKnown: extra.UsageKnown ? null : false))
                .ToArray();

        return new CliUsagePayload(
            snapshot.Primary is { } primary ? BuildWindow(primary) : null,
            snapshot.Secondary is { } secondary ? BuildWindow(secondary) : null,
            extras,
            Timestamp(snapshot.UpdatedAt),
            snapshot.Identity is { } identity
                ? new CliIdentityPayload(
                    identity.AccountLabel,
                    identity.Organization,
                    identity.PlanName)
                : null);
    }

    private static CliRateWindowPayload BuildWindow(RateWindow window) =>
        new(
            window.UsedPercent,
            window.Window is { } span ? (int)Math.Round(span.TotalMinutes) : null,
            window.ResetsAt is { } resetsAt ? Timestamp(resetsAt) : null,
            window.ResetDescription,
            window.Title,
            window.QuotaLimit,
            window.QuotaUnit,
            window.IsSyntheticPlaceholder);

    private static CliWindowLabelsPayload? BuildLabels(UsageSnapshot snapshot)
    {
        var primary = snapshot.Primary is { } p ? p.Title ?? "Session" : null;
        var secondary = snapshot.Secondary is { } s ? s.Title ?? "Weekly" : null;
        return primary is null && secondary is null
            ? null
            : new CliWindowLabelsPayload(primary, secondary, Tertiary: null);
    }

    private static string CamelCase(string value) =>
        value.Length == 0
            ? value
            : string.Create(CultureInfo.InvariantCulture, $"{char.ToLowerInvariant(value[0])}{value[1..]}");
}
