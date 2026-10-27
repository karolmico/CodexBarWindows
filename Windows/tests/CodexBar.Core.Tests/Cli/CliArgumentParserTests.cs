using CodexBar.Core.Cli;
using Xunit;

namespace CodexBar.Core.Tests.Cli;

public sealed class CliArgumentParserTests
{
    [Fact]
    public void Empty_arguments_default_to_usage_text()
    {
        var invocation = CliArgumentParser.Parse([]);

        Assert.Null(invocation.Error);
        Assert.Equal(CliCommand.Usage, invocation.Command);
        Assert.Equal(CliOutputFormat.Text, invocation.Format);
        Assert.Empty(invocation.Providers);
        Assert.False(invocation.Pretty);
        Assert.False(invocation.RequireAll);
    }

    [Fact]
    public void Parses_every_supported_option()
    {
        var invocation = CliArgumentParser.Parse(
            ["usage", "--format", "json", "--provider", "codex,claude", "--pretty", "--require-all"]);

        Assert.Null(invocation.Error);
        Assert.Equal(CliOutputFormat.Json, invocation.Format);
        Assert.Equal(["codex", "claude"], invocation.Providers.Select(key => key.Value));
        Assert.True(invocation.Pretty);
        Assert.True(invocation.RequireAll);
    }

    [Fact]
    public void Json_flag_is_a_format_shortcut()
    {
        Assert.Equal(CliOutputFormat.Json, CliArgumentParser.Parse(["--json"]).Format);
    }

    [Fact]
    public void Repeated_provider_options_keep_the_given_order_and_drop_duplicates()
    {
        var invocation = CliArgumentParser.Parse(
            ["--provider", "cursor", "-p", "codex", "-p", "cursor"]);

        Assert.Equal(["cursor", "codex"], invocation.Providers.Select(key => key.Value));
    }

    [Fact]
    public void Help_and_version_win_over_usage()
    {
        Assert.Equal(CliCommand.Help, CliArgumentParser.Parse(["--help"]).Command);
        Assert.Equal(CliCommand.Help, CliArgumentParser.Parse(["usage", "-h"]).Command);
        Assert.Equal(CliCommand.Version, CliArgumentParser.Parse(["-V"]).Command);
    }

    [Theory]
    [InlineData("usage --format toon", "Unknown format 'toon'")]
    [InlineData("usage --format", "needs a value")]
    [InlineData("usage --provider", "needs a value")]
    [InlineData("usage --provider ,", "at least one provider id")]
    [InlineData("usage --provider Codex", "not a valid provider id")]
    [InlineData("usage --bogus", "Unknown option '--bogus'")]
    [InlineData("serve", "Unknown command 'serve'")]
    [InlineData("usage extra", "Unexpected argument 'extra'")]
    public void Rejects_bad_input_with_a_usage_error(string line, string expected)
    {
        var invocation = CliArgumentParser.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.NotNull(invocation.Error);
        Assert.Contains(expected, invocation.Error, StringComparison.Ordinal);
    }
}
