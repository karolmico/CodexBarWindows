using CodexBar.Core.Formatting;
using CodexBar.Core.Presentation;

namespace CodexBar.Core.Setup;

/// <summary>One line describing the result of the last connection check in the setup window.</summary>
public sealed record ProviderSetupStatus(string Text, bool IsConnected, bool IsProblem)
{
    public static ProviderSetupStatus Unknown { get; } = new("Not checked yet.", false, false);
}

/// <summary>
/// Turns coordinator state into the copy under "Check connection". Kept in Core so the wording is
/// covered by tests and never drifts from the status labels used by the tray.
/// </summary>
public static class ProviderSetupStatusText
{
    public static ProviderSetupStatus Describe(ProviderState? state, DateTimeOffset now)
    {
        if (state is null || (state.Snapshot is null && state.Error is null))
        {
            return ProviderSetupStatus.Unknown;
        }

        if (state.Error is { } error)
        {
            return new ProviderSetupStatus(
                $"{ProviderStatusText.Label(error)}: {error.Message}",
                IsConnected: false,
                IsProblem: true);
        }

        if (state.Snapshot is not { } snapshot)
        {
            return new ProviderSetupStatus(
                "Connected, but no usage reported yet.",
                IsConnected: true,
                IsProblem: false);
        }

        var detail = snapshot.HasMeasuredWindows
            ? "Usage data received."
            : "Connected, but this account reports no quota windows.";
        return new ProviderSetupStatus(
            $"Connected. {detail} Updated {UsageFormatter.FormatCountdown(now - snapshot.UpdatedAt)} ago.",
            IsConnected: true,
            IsProblem: false);
    }
}
