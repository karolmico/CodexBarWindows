using CodexBar.Core.Providers;
using CodexBar.Core.Usage;

namespace CodexBar.Core.Cli;

/// <summary>Exit codes shared by the console host and its tests.</summary>
public enum CliExitCode
{
    /// <summary>Every selected provider produced a snapshot.</summary>
    Success = 0,

    /// <summary>At least one provider failed while others succeeded; output is still complete.</summary>
    PartialFailure = 1,

    /// <summary>No selected provider produced a snapshot, or no provider was selected.</summary>
    TotalFailure = 2,

    /// <summary>Bad command line. Mirrors the conventional EX_USAGE code.</summary>
    UsageError = 64,
}

public static class CliExitCodes
{
    /// <summary>
    /// Maps fetch results to an exit code. A partial failure stays distinct from a total failure so
    /// scripts can retry only when nothing usable was printed. <paramref name="requireAll"/> turns any
    /// failure into a total failure for callers that cannot tolerate gaps.
    /// </summary>
    public static CliExitCode FromResults(IReadOnlyList<CliProviderResult> results, bool requireAll = false)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return CliExitCode.TotalFailure;
        }

        var succeeded = results.Count(static result => result.IsSuccess);
        if (succeeded == results.Count)
        {
            return CliExitCode.Success;
        }

        if (requireAll || succeeded == 0)
        {
            return CliExitCode.TotalFailure;
        }

        return CliExitCode.PartialFailure;
    }
}

/// <summary>One provider's outcome for a single CLI run.</summary>
public sealed record CliProviderResult(
    ProviderKey Key,
    string DisplayName,
    string Source,
    UsageSnapshot? Snapshot,
    ProviderError? Error,
    TimeSpan? Duration)
{
    public bool IsSuccess => this.Snapshot is not null && this.Error is null;
}

/// <summary>Stable source labels used in the CLI header and JSON payload.</summary>
public static class CliSources
{
    public static string For(ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Value switch
        {
            "codex" => "codex-cli",
            "claude" => "claude-cli",
            "gemini" => "gemini-cli",
            "copilot" => "gh-cli",
            "cursor" => "cursor-app",
            "antigravity" => "antigravity-app",
            "opencode" => "opencode-api",
            _ => "auto",
        };
    }
}
