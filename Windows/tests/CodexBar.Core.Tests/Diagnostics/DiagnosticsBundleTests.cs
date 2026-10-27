using System.Text.Json;
using CodexBar.Core.Diagnostics;
using CodexBar.Core.Identity;
using CodexBar.Core.Presentation;
using CodexBar.Core.Providers;
using CodexBar.Core.Refresh;
using CodexBar.Core.Settings;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests.Diagnostics;

public sealed class DiagnosticsBundleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly DiagnosticsEnvironment Environment = new(
        "0.1.0",
        ".NET 9.0.0",
        "Windows 10.0.26100");

    [Fact]
    public void Contains_versions_settings_shape_keys_errors_and_timings()
    {
        var settings = new AppSettings(
            [
                new ProviderPreference(ProviderKeys.Codex),
                new ProviderPreference(ProviderKeys.Claude, enabled: false),
            ],
            TimeSpan.FromMinutes(4),
            notificationThresholds: [70, 90],
            notifyResets: false,
            quietHoursStart: new TimeOnly(22, 0),
            quietHoursEnd: new TimeOnly(7, 0),
            startWithWindows: true);

        var states = new[]
        {
            new ProviderState(
                ProviderKeys.Codex,
                "Codex",
                Snapshot(ProviderKeys.Codex, session: 42d),
                ProviderError.AuthenticationRequired("expired token for dev@example.com")),
            new ProviderState(ProviderKeys.Claude, "Claude"),
        };

        var timings = new[]
        {
            new ProviderTiming(ProviderKeys.Codex, TimeSpan.FromMilliseconds(123.45), Now),
            new ProviderTiming(ProviderKeys.Claude, LastFetchDuration: null, LastFetchAt: null),
        };

        var json = DiagnosticsBundle.Build(settings, states, timings, Environment, Now);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("2026-10-27T12:00:00Z", root.GetProperty("timestamp").GetString());

        var app = root.GetProperty("app");
        Assert.Equal("CodexBar", app.GetProperty("name").GetString());
        Assert.Equal("0.1.0", app.GetProperty("version").GetString());
        Assert.Equal(".NET 9.0.0", app.GetProperty("runtime").GetString());
        Assert.Equal("windows", app.GetProperty("platform").GetString());

        var settingsJson = root.GetProperty("settings");
        Assert.Equal(4, settingsJson.GetProperty("refreshFloorMinutes").GetInt32());
        Assert.Equal([70, 90], settingsJson.GetProperty("notificationThresholds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.False(settingsJson.GetProperty("notifyResets").GetBoolean());
        Assert.Equal("22:00", settingsJson.GetProperty("quietHoursStart").GetString());
        Assert.True(settingsJson.GetProperty("startWithWindows").GetBoolean());
        Assert.Equal(
            ["codex"],
            settingsJson.GetProperty("enabledProviderKeys").EnumerateArray().Select(v => v.GetString()));

        var providers = root.GetProperty("providers").EnumerateArray().ToArray();
        Assert.Equal(2, providers.Length);
        Assert.Equal("error", providers[0].GetProperty("state").GetString());
        Assert.Equal("authenticationRequired", providers[0].GetProperty("lastErrorKind").GetString());
        Assert.Equal(123.5, providers[0].GetProperty("lastFetchMilliseconds").GetDouble());
        Assert.Equal("noData", providers[1].GetProperty("state").GetString());
        Assert.Equal("claude", providers[1].GetProperty("key").GetString());
        Assert.False(providers[1].GetProperty("enabled").GetBoolean());
        Assert.False(providers[1].TryGetProperty("lastFetchMilliseconds", out _));
    }

    [Fact]
    public void Never_includes_accounts_messages_tokens_or_paths()
    {
        var states = new[]
        {
            new ProviderState(
                ProviderKeys.Codex,
                "Codex",
                Snapshot(ProviderKeys.Codex, session: 42d),
                new ProviderError(
                    ProviderErrorKind.Network,
                    "failed for dev@example.com with token=abc123 at C:\\Users\\dev\\auth.json")),
        };

        var settings = new AppSettings([new ProviderPreference(ProviderKeys.Codex)]);
        var json = DiagnosticsBundle.Build(
            settings,
            states,
            [new ProviderTiming(ProviderKeys.Codex, TimeSpan.FromMilliseconds(5), Now)],
            Environment,
            Now);

        Assert.DoesNotContain("dev@example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users", json, StringComparison.Ordinal);
        Assert.DoesNotContain("auth.json", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", json, StringComparison.Ordinal);
        Assert.DoesNotContain("work@contoso.test", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Quiet_hours_are_omitted_when_disabled()
    {
        var settings = new AppSettings([new ProviderPreference(ProviderKeys.Codex)]);
        var json = DiagnosticsBundle.Build(settings, [], [], Environment, Now);
        using var document = JsonDocument.Parse(json);

        var settingsJson = document.RootElement.GetProperty("settings");
        Assert.False(settingsJson.TryGetProperty("quietHoursStart", out _));
        Assert.False(settingsJson.TryGetProperty("quietHoursEnd", out _));
        Assert.Empty(document.RootElement.GetProperty("providers").EnumerateArray());
    }

    [Fact]
    public void File_name_is_timestamped_and_stable()
    {
        Assert.Equal("diagnostics-20261027-120000.json", DiagnosticsBundle.FileName(Now));
    }

    private static UsageSnapshot Snapshot(ProviderKey key, double session) =>
        new(
            key,
            Now.AddMinutes(-2),
            new RateWindow(session, TimeSpan.FromHours(5), Now.AddHours(3)),
            new RateWindow(session / 2, TimeSpan.FromDays(7), Now.AddDays(2)),
            identity: new ProviderIdentity("Plus", "work@contoso.test", "Contoso"));
}
