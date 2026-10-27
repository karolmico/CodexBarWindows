using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBar.Core.Cli;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Refresh;
using CodexBar.Core.Settings;

namespace CodexBar.Core.Diagnostics;

/// <summary>Facts the host knows about itself; injected so the bundle stays testable.</summary>
public sealed record DiagnosticsEnvironment(
    string AppVersion,
    string RuntimeVersion,
    string OperatingSystem);

/// <summary>
/// Builds the redacted bundle a user attaches to a bug report. It contains versions, settings shape,
/// enabled provider keys, error kinds, and timings — never tokens, cookies, emails, account labels,
/// messages, or file paths. The final JSON is passed through <see cref="LogRedactor"/> as a safety net.
/// </summary>
public static class DiagnosticsBundle
{
    public const int SchemaVersion = 1;

    public static string Build(
        AppSettings settings,
        IReadOnlyList<ProviderState> states,
        IReadOnlyList<ProviderTiming> timings,
        DiagnosticsEnvironment environment,
        DateTimeOffset now,
        bool pretty = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(timings);
        ArgumentNullException.ThrowIfNull(environment);

        var timingsByKey = timings.ToDictionary(static timing => timing.Key);
        var enabledKeys = settings.EnabledProviderKeys.ToArray();

        var document = new BundleDocument(
            SchemaVersion,
            CliPayloadBuilder.Timestamp(now),
            new AppSection(
                "CodexBar",
                environment.AppVersion,
                environment.RuntimeVersion,
                environment.OperatingSystem,
                "windows"),
            new SettingsSection(
                settings.SchemaVersion,
                (int)Math.Round(settings.RefreshFloor.TotalMinutes),
                settings.NotificationThresholds.ToArray(),
                settings.NotifyResets,
                settings.QuietHoursStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
                settings.QuietHoursEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
                settings.StartWithWindows,
                enabledKeys.Select(static key => key.Value).ToArray()),
            states.Select(state => DescribeProvider(state, timingsByKey, enabledKeys)).ToArray());

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = pretty,
        };

        return LogRedactor.Redact(JsonSerializer.Serialize(document, options));
    }

    public static string FileName(DateTimeOffset now) =>
        $"diagnostics-{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";

    private static ProviderSection DescribeProvider(
        ProviderState state,
        IReadOnlyDictionary<ProviderKey, ProviderTiming> timings,
        IReadOnlyList<ProviderKey> enabledKeys)
    {
        timings.TryGetValue(state.Key, out var timing);
        var stateLabel = state.Error is not null
            ? "error"
            : state.Snapshot is not null
                ? "ok"
                : "noData";

        return new ProviderSection(
            state.Key.Value,
            state.DisplayName,
            enabledKeys.Contains(state.Key),
            stateLabel,
            state.Error is { } error ? CamelCase(error.Kind.ToString()) : null,
            state.Snapshot is { } snapshot ? CliPayloadBuilder.Timestamp(snapshot.UpdatedAt) : null,
            state.Snapshot?.MeasuredWindows.Count() ?? 0,
            timing?.LastFetchDuration is { } duration
                ? Math.Round(duration.TotalMilliseconds, 1, MidpointRounding.AwayFromZero)
                : null,
            timing?.LastFetchAt is { } fetchAt ? CliPayloadBuilder.Timestamp(fetchAt) : null);
    }

    private static string CamelCase(string value) =>
        value.Length == 0
            ? value
            : string.Create(CultureInfo.InvariantCulture, $"{char.ToLowerInvariant(value[0])}{value[1..]}");

    private sealed record BundleDocument(
        int SchemaVersion,
        string Timestamp,
        AppSection App,
        SettingsSection Settings,
        IReadOnlyList<ProviderSection> Providers);

    private sealed record AppSection(
        string Name,
        string Version,
        string Runtime,
        string OperatingSystem,
        string Platform);

    private sealed record SettingsSection(
        int SchemaVersion,
        int RefreshFloorMinutes,
        IReadOnlyList<int> NotificationThresholds,
        bool NotifyResets,
        string? QuietHoursStart,
        string? QuietHoursEnd,
        bool StartWithWindows,
        IReadOnlyList<string> EnabledProviderKeys);

    private sealed record ProviderSection(
        string Key,
        string DisplayName,
        bool Enabled,
        string State,
        string? LastErrorKind,
        string? LastUpdatedAt,
        int WindowCount,
        double? LastFetchMilliseconds,
        string? LastFetchAt);
}
