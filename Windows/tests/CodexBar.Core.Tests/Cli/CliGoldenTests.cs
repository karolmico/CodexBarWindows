using System.IO;
using CodexBar.Core.Cli;
using CodexBar.Core.Identity;
using CodexBar.Core.Providers;
using CodexBar.Core.Usage;
using Xunit;

namespace CodexBar.Core.Tests.Cli;

/// <summary>
/// Locks the exact JSON and text contract. Fixtures live under <c>Fixtures/Cli</c> and are copied to the
/// test output, so a field rename or a layout change fails loudly.
/// </summary>
public sealed class CliGoldenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Json_output_matches_the_golden_file()
    {
        var actual = CliPayloadBuilder.ToJson(CliPayloadBuilder.Build(Fixture()), pretty: true);

        Assert.Equal(Golden("usage.json"), actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Text_output_matches_the_golden_file()
    {
        var actual = CliTextRenderer.Render(Fixture(), Now);

        Assert.Equal(Golden("usage.txt"), actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Compact_json_has_no_pretty_printing()
    {
        var compact = CliPayloadBuilder.ToJson(CliPayloadBuilder.Build(Fixture()), pretty: false);

        Assert.DoesNotContain('\n', compact);
        Assert.Contains("\"provider\":\"codex\"", compact, StringComparison.Ordinal);
        Assert.Contains("\"providerErrorKind\":\"notConfigured\"", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_keeps_raw_percentages_and_reset_instants()
    {
        var payload = CliPayloadBuilder.Build(Fixture());

        var codex = payload[0];
        Assert.Equal("codex", codex.Provider);
        Assert.Equal("codex-cli", codex.Source);
        Assert.Equal(42d, codex.Usage!.Primary!.UsedPercent);
        Assert.Equal(300, codex.Usage.Primary.WindowMinutes);
        Assert.Equal("2026-10-27T15:00:00Z", codex.Usage.Primary.ResetsAt);
        Assert.Equal("2026-10-27T12:00:00Z", codex.Usage.UpdatedAt);
        Assert.Equal("Plus", codex.Usage.Identity!.Plan);
        Assert.Null(codex.Error);

        var gemini = payload[2];
        Assert.Null(gemini.Usage);
        Assert.Equal("notConfigured", gemini.Error!.ProviderErrorKind);
        Assert.Equal("provider", gemini.Error.Kind);
    }

    [Fact]
    public void Synthetic_windows_stay_flagged_in_json_and_out_of_text()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Cursor,
            Now,
            primary: new RateWindow(0d, TimeSpan.FromHours(5), isSyntheticPlaceholder: true));
        var result = new CliProviderResult(
            ProviderKeys.Cursor,
            "Cursor",
            "cursor-app",
            snapshot,
            Error: null,
            TimeSpan.Zero);

        var json = CliPayloadBuilder.ToJson(CliPayloadBuilder.Build([result]), pretty: false);
        var text = CliTextRenderer.Render([result], Now);

        Assert.Contains("\"isSyntheticPlaceholder\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Session:", text, StringComparison.Ordinal);
        Assert.Equal("== Cursor (cursor-app) ==", text);
    }

    [Fact]
    public void Unknown_named_windows_are_unavailable_instead_of_zero()
    {
        var snapshot = new UsageSnapshot(
            ProviderKeys.Gemini,
            Now,
            extraWindows:
            [
                new NamedRateWindow(
                    "gemini-3-pro",
                    "Gemini 3 Pro",
                    new RateWindow(0d, TimeSpan.FromDays(1)),
                    usageKnown: false),
            ]);
        var result = new CliProviderResult(
            ProviderKeys.Gemini,
            "Gemini",
            "gemini-cli",
            snapshot,
            Error: null,
            TimeSpan.Zero);

        var text = CliTextRenderer.Render([result], Now);
        var json = CliPayloadBuilder.ToJson(CliPayloadBuilder.Build([result]), pretty: false);

        Assert.Contains("Gemini 3 Pro: Unavailable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0% left", text, StringComparison.Ordinal);
        Assert.Contains("\"usageKnown\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Remaining_text_uses_upstream_thresholds()
    {
        Assert.Equal("42% left", CliTextRenderer.RemainingText(58d));
        Assert.Equal("<1% left", CliTextRenderer.RemainingText(99.5d));
        Assert.Equal("0% left", CliTextRenderer.RemainingText(100d));
        Assert.Equal("0% left", CliTextRenderer.RemainingText(120d));
        Assert.Equal("[------------]", CliTextRenderer.Bar(100d));
        Assert.Equal("[============]", CliTextRenderer.Bar(0d));
    }

    private static string Golden(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cli", name))
            .ReplaceLineEndings("\n")
            .TrimEnd('\n');

    private static IReadOnlyList<CliProviderResult> Fixture()
    {
        var codex = new UsageSnapshot(
            ProviderKeys.Codex,
            Now,
            new RateWindow(42d, TimeSpan.FromHours(5), Now.AddHours(3)),
            new RateWindow(61d, TimeSpan.FromDays(7), Now.AddDays(2)),
            identity: new ProviderIdentity("Plus", "work", "Contoso"));

        var claude = new UsageSnapshot(
            ProviderKeys.Claude,
            Now,
            new RateWindow(90d, TimeSpan.FromHours(5), Now.AddHours(4)),
            new RateWindow(30d, TimeSpan.FromDays(7), Now.AddDays(3)),
            [
                new NamedRateWindow(
                    "opus",
                    "Opus",
                    new RateWindow(12d, TimeSpan.FromDays(1), resetDescription: "Resets after top-up")),
            ],
            new ProviderIdentity("Max"));

        return
        [
            new(ProviderKeys.Codex, "Codex", CliSources.For(ProviderKeys.Codex), codex, null, TimeSpan.FromMilliseconds(120)),
            new(ProviderKeys.Claude, "Claude", CliSources.For(ProviderKeys.Claude), claude, null, TimeSpan.FromMilliseconds(80)),
            new(
                ProviderKeys.Gemini,
                "Gemini",
                CliSources.For(ProviderKeys.Gemini),
                null,
                ProviderError.NotConfigured("Gemini credentials not found. Run `gemini` to sign in."),
                TimeSpan.FromMilliseconds(5)),
        ];
    }
}
