using CodexBar.Core.Providers;

namespace CodexBar.Core.Cli;

public enum CliCommand
{
    Usage,
    Help,
    Version,
}

public enum CliOutputFormat
{
    Text,
    Json,
}

/// <summary>A parsed command line. <see cref="Error"/> is set instead of throwing so the host can print help.</summary>
public sealed record CliInvocation
{
    public CliCommand Command { get; init; } = CliCommand.Usage;

    public CliOutputFormat Format { get; init; } = CliOutputFormat.Text;

    /// <summary>Provider filters in the order given; empty means "every enabled provider".</summary>
    public IReadOnlyList<ProviderKey> Providers { get; init; } = [];

    public bool RequireAll { get; init; }

    public bool Pretty { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Parses the small option surface shared with the macOS CLI: <c>usage</c>, <c>--format</c>,
/// <c>--provider</c>, <c>--pretty</c>. Unknown options fail with a usage error rather than being ignored.
/// </summary>
public static class CliArgumentParser
{
    public static CliInvocation Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = CliCommand.Usage;
        var format = CliOutputFormat.Text;
        var providers = new List<ProviderKey>();
        var requireAll = false;
        var pretty = false;

        var index = 0;
        if (args.Count > 0 && !args[0].StartsWith('-'))
        {
            switch (args[0])
            {
                case "usage":
                    command = CliCommand.Usage;
                    break;
                case "help":
                    command = CliCommand.Help;
                    break;
                case "version":
                    command = CliCommand.Version;
                    break;
                default:
                    return new CliInvocation
                    {
                        Error = $"Unknown command '{args[0]}'. Try 'codexbar usage'.",
                    };
            }

            index = 1;
        }

        for (; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h":
                    command = CliCommand.Help;
                    break;
                case "--version" or "-V":
                    command = CliCommand.Version;
                    break;
                case "--json":
                    format = CliOutputFormat.Json;
                    break;
                case "--pretty":
                    pretty = true;
                    break;
                case "--require-all":
                    requireAll = true;
                    break;
                case "--format" or "-f":
                    if (!TryTakeValue(args, ref index, arg, out var formatValue, out var formatError))
                    {
                        return new CliInvocation { Error = formatError };
                    }

                    switch (formatValue.ToLowerInvariant())
                    {
                        case "text":
                            format = CliOutputFormat.Text;
                            break;
                        case "json":
                            format = CliOutputFormat.Json;
                            break;
                        default:
                            return new CliInvocation
                            {
                                Error = $"Unknown format '{formatValue}'. Expected 'text' or 'json'.",
                            };
                    }

                    break;
                case "--provider" or "-p":
                    if (!TryTakeValue(args, ref index, arg, out var providerValue, out var providerError))
                    {
                        return new CliInvocation { Error = providerError };
                    }

                    var before = providers.Count;
                    foreach (var part in providerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!ProviderKey.TryParse(part, out var key))
                        {
                            return new CliInvocation { Error = $"'{part}' is not a valid provider id." };
                        }

                        providers.Add(key);
                    }

                    if (providers.Count == before)
                    {
                        return new CliInvocation { Error = "Option '--provider' needs at least one provider id." };
                    }

                    break;
                default:
                    return new CliInvocation
                    {
                        Error = arg.StartsWith('-')
                            ? $"Unknown option '{arg}'."
                            : $"Unexpected argument '{arg}'.",
                    };
            }
        }

        return new CliInvocation
        {
            Command = command,
            Format = format,
            Providers = providers.Distinct().ToArray(),
            RequireAll = requireAll,
            Pretty = pretty,
        };
    }

    private static bool TryTakeValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
        {
            error = $"Option '{option}' needs a value.";
            return false;
        }

        value = args[++index];
        return true;
    }
}
