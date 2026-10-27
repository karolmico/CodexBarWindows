using CodexBar.Core.Providers;

namespace CodexBar.Core.Presentation;

/// <summary>
/// The mark and accent a provider gets in the flyout. The glyph is SVG-style path data in a 16x16
/// box so the window can draw it with a <c>Path</c> and needs no icon files or font dependencies.
/// </summary>
public sealed record ProviderVisual(string IconPath, string Accent)
{
    public static ProviderVisual Fallback { get; } = new(
        "M8,1.6 L8,14.4 M1.6,8 L14.4,8",
        "#475569");
}

/// <summary>One place for provider marks, so tabs and any later surface stay identical.</summary>
public static class ProviderVisuals
{
    public static ProviderVisual For(ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Value switch
        {
            // Rounded hexagon: the Codex "knot" reduced to a line mark.
            "codex" => new("M8,1.3 L13.8,4.6 L13.8,11.4 L8,14.7 L2.2,11.4 L2.2,4.6 Z", "#1F2937"),

            // Six-spoke star: the Claude burst.
            "claude" => new("M8,1.6 L8,14.4 M2.5,4.8 L13.5,11.2 M13.5,4.8 L2.5,11.2", "#C2410C"),

            // Robot head with antennae: GitHub Copilot.
            "copilot" => new(
                "M3.2,6.2 H12.8 V13 H3.2 Z M6,6.2 V4.2 A2,2 0 0 1 10,4.2 V6.2 M1.4,8.6 V11 M14.6,8.6 V11",
                "#7C3AED"),

            // Pointer arrow: the Cursor editor mark.
            "cursor" => new("M3,1.6 L13.2,9.2 L8.6,9.8 L10.9,14.4 L8.9,15.4 L6.6,10.8 L3,13.2 Z", "#0F766E"),

            // Four-point sparkle: the Antigravity star.
            "antigravity" => new("M8,1.2 L9.5,6.5 L14.8,8 L9.5,9.5 L8,14.8 L6.5,9.5 L1.2,8 L6.5,6.5 Z", "#0284C7"),

            // Angle brackets: OpenCode.
            "opencode" => new("M6,2.2 L1.6,8 L6,13.8 M10,2.2 L14.4,8 L10,13.8", "#4F46E5"),

            _ => ProviderVisual.Fallback,
        };
    }
}
