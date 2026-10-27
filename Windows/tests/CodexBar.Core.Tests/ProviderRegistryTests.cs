using CodexBar.Core.Providers;
using CodexBar.Core.Tests.Support;
using Xunit;

namespace CodexBar.Core.Tests;

public sealed class ProviderRegistryTests
{
    private static ProviderRegistry Registry(params ProviderKey[] keys) =>
        new(keys.Select(key => TestSnapshots.StubProvider(key, key.Value.ToUpperInvariant())));

    [Fact]
    public void Resolves_providers_in_the_configured_order()
    {
        var registry = Registry(ProviderKeys.Codex, ProviderKeys.Claude, ProviderKeys.Copilot);

        var resolved = registry.Resolve([ProviderKeys.Copilot, ProviderKeys.Codex]);

        Assert.Equal(["copilot", "codex"], resolved.Select(provider => provider.Descriptor.Key.Value));
    }

    [Fact]
    public void Skips_unknown_keys_without_dropping_the_rest()
    {
        var registry = Registry(ProviderKeys.Codex, ProviderKeys.Claude);

        var resolved = registry.Resolve([ProviderKey.Parse("retired-provider"), ProviderKeys.Claude]);

        Assert.Single(resolved);
        Assert.Equal("claude", resolved[0].Descriptor.Key.Value);
    }

    [Fact]
    public void Duplicate_configuration_entries_keep_the_first_position()
    {
        var registry = Registry(ProviderKeys.Codex, ProviderKeys.Claude);

        var resolved = registry.Resolve([ProviderKeys.Claude, ProviderKeys.Codex, ProviderKeys.Claude]);

        Assert.Equal(["claude", "codex"], resolved.Select(provider => provider.Descriptor.Key.Value));
    }

    [Fact]
    public void Empty_configuration_resolves_to_no_providers()
    {
        var registry = Registry(ProviderKeys.Codex);

        Assert.Empty(registry.Resolve([]));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Duplicate_registration_is_rejected()
    {
        var duplicate = TestSnapshots.StubProvider(ProviderKeys.Codex, "Codex again");

        var error = Assert.Throws<ArgumentException>(() =>
            new ProviderRegistry([TestSnapshots.StubProvider(ProviderKeys.Codex, "Codex"), duplicate]));

        Assert.Contains("codex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_reports_missing_providers()
    {
        var registry = Registry(ProviderKeys.Codex);

        Assert.True(registry.TryGet(ProviderKeys.Codex, out _));
        Assert.False(registry.TryGet(ProviderKeys.Claude, out _));
    }
}
