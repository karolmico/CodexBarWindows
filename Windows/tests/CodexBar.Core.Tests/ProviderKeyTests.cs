using CodexBar.Core.Providers;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class ProviderKeyTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("open-code")]
    [InlineData("provider2")]
    public void Parses_canonical_lowercase_keys(string value)
    {
        var key = ProviderKey.Parse(value);

        Assert.Equal(value, key.Value);
        Assert.Equal(value, key.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Codex")]
    [InlineData("CODEX")]
    [InlineData("-codex")]
    [InlineData("codex bar")]
    [InlineData("codex/bar")]
    [InlineData("codex_bar")]
    public void Rejects_keys_that_are_not_canonical(string value)
    {
        Assert.False(ProviderKey.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => ProviderKey.Parse(value));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(ProviderKey.TryParse(null, out _));
    }

    [Fact]
    public void Keys_compare_by_value()
    {
        Assert.Equal(ProviderKey.Parse("codex"), ProviderKey.Parse("codex"));
        Assert.NotEqual(ProviderKey.Parse("codex"), ProviderKey.Parse("claude"));
    }

    [Fact]
    public void Shipped_keys_match_upstream_ids()
    {
        Assert.Equal("codex", ProviderKeys.Codex.Value);
        Assert.Equal("claude", ProviderKeys.Claude.Value);
        Assert.Equal("cursor", ProviderKeys.Cursor.Value);
        Assert.Equal("opencode", ProviderKeys.OpenCode.Value);
        Assert.Equal("copilot", ProviderKeys.Copilot.Value);
        Assert.Equal("antigravity", ProviderKeys.Antigravity.Value);
    }
}
