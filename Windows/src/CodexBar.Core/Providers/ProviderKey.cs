namespace CodexBar.Core.Providers;

/// <summary>
/// Canonical provider identifier. Lowercase, hyphenated, and stable across platforms so
/// configuration, CLI output, and upstream documentation stay compatible.
/// </summary>
public sealed record ProviderKey
{
    private ProviderKey(string value) => this.Value = value;

    public string Value { get; }

    public static ProviderKey Parse(string value) =>
        TryParse(value, out var key)
            ? key
            : throw new ArgumentException(
                $"'{value}' is not a valid provider key. Expected lowercase letters, digits, and hyphens.",
                nameof(value));

    public static bool TryParse(string? value, out ProviderKey key)
    {
        key = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length > 64 || value[0] is '-' or '_')
        {
            return false;
        }

        foreach (var character in value)
        {
            var valid = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (!valid)
            {
                return false;
            }
        }

        key = new ProviderKey(value);
        return true;
    }

    public override string ToString() => this.Value;
}

/// <summary>Provider keys shipped in the first release (Linux parity set).</summary>
public static class ProviderKeys
{
    public static readonly ProviderKey Codex = ProviderKey.Parse("codex");
    public static readonly ProviderKey Claude = ProviderKey.Parse("claude");
    public static readonly ProviderKey Cursor = ProviderKey.Parse("cursor");
    public static readonly ProviderKey Copilot = ProviderKey.Parse("copilot");
    public static readonly ProviderKey Antigravity = ProviderKey.Parse("antigravity");
    public static readonly ProviderKey OpenCode = ProviderKey.Parse("opencode");
}
