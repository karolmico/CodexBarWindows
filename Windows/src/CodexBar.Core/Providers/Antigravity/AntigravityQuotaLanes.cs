using System.Globalization;
using CodexBar.Core.Identity;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers.Antigravity;

/// <summary>
/// One quota lane as reported by an Antigravity source (the local language server or the
/// <c>agy</c> CLI), before it is ranked into a snapshot. Both sources share this shape so the
/// Gemini/Claude+GPT lane mapping can never drift between them.
/// </summary>
internal sealed record AntigravityQuotaLane(
    string GroupName,
    string BucketId,
    string BucketName,
    string Cadence,
    double? RemainingFraction,
    DateTimeOffset? ResetsAt,
    bool UsageKnown)
{
    public string Family => this.GroupName.Contains("gemini", StringComparison.OrdinalIgnoreCase)
        && !this.GroupName.Any(char.IsDigit)
            ? "Gemini"
            : this.GroupName.Contains("claude", StringComparison.OrdinalIgnoreCase)
                || this.GroupName.Contains("gpt", StringComparison.OrdinalIgnoreCase)
                    ? "Claude/GPT"
                    : this.GroupName;

    public string Title => $"{this.Family} {this.Cadence switch
    {
        "Session" => "5-hour",
        "Weekly" => "weekly",
        _ => this.BucketName,
    }}";

    public double UsedPercent => Math.Clamp((1d - (this.RemainingFraction ?? 1d)) * 100d, 0d, 200d);

    public RateWindow ToRateWindow()
    {
        var window = this.Cadence switch
        {
            "Session" => TimeSpan.FromMinutes(300),
            "Weekly" => TimeSpan.FromMinutes(10080),
            _ => (TimeSpan?)null,
        };

        return new RateWindow(this.UsedPercent, window, this.ResetsAt);
    }
}

/// <summary>
/// Ranks reported lanes into the snapshot's primary (Gemini), secondary (Claude/GPT) and extras.
/// </summary>
internal static class AntigravityQuotaSnapshot
{
    public static UsageSnapshot Build(
        IReadOnlyList<AntigravityQuotaLane> lanes,
        DateTimeOffset now,
        ProviderIdentity? identity = null)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        var ranked = lanes
            .Where(static lane => lane.UsageKnown)
            .OrderByDescending(static lane => lane.UsedPercent)
            .ToList();
        if (ranked.Count == 0)
        {
            throw new ArgumentException("At least one lane with a usage value is required.", nameof(lanes));
        }

        var gemini = ranked.FirstOrDefault(static lane => lane.Family == "Gemini");
        var claudeGpt = ranked.FirstOrDefault(static lane => lane.Family == "Claude/GPT");
        var primaryLane = gemini ?? ranked[0];
        var secondaryLane = claudeGpt is not null && !ReferenceEquals(claudeGpt, primaryLane)
            ? claudeGpt
            : null;

        var extras = new List<NamedRateWindow>();
        foreach (var lane in lanes)
        {
            if (ReferenceEquals(lane, primaryLane) || ReferenceEquals(lane, secondaryLane))
            {
                continue;
            }

            extras.Add(new NamedRateWindow(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"antigravity-{Slug(lane.Family)}-{Slug(lane.BucketId)}"),
                lane.Title,
                lane.ToRateWindow(),
                lane.UsageKnown));
        }

        return new UsageSnapshot(
            ProviderKeys.Antigravity,
            now,
            primaryLane.ToRateWindow(),
            secondaryLane?.ToRateWindow(),
            extras,
            identity);
    }

    public static string Slug(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(characters).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "quota" : slug;
    }

    public static string ClassifyCadence(string? explicitWindow, string bucketId, string bucketName)
    {
        var normalised = explicitWindow?.Trim().ToLowerInvariant().Replace('_', '-');
        var candidates = string.IsNullOrEmpty(normalised)
            ? new[] { bucketId.ToLowerInvariant().Replace('_', '-'), bucketName.ToLowerInvariant().Replace('_', '-') }
            : [normalised];

        foreach (var candidate in candidates)
        {
            var value = candidate.EndsWith(" limit", StringComparison.Ordinal)
                ? candidate[..^" limit".Length]
                : candidate;
            if (value is "session" or "5h" or "5-hour" or "five hour" or "five-hour"
                || value.EndsWith("-session", StringComparison.Ordinal)
                || value.EndsWith("-5h", StringComparison.Ordinal))
            {
                return "Session";
            }

            if (value == "weekly" || value.EndsWith("-weekly", StringComparison.Ordinal))
            {
                return "Weekly";
            }
        }

        return "Other";
    }
}
