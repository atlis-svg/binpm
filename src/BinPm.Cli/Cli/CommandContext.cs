using BinPm.Core;

namespace BinPm.Cli.Cli;

/// <summary>The parsed result for a single command invocation.</summary>
public sealed class CommandContext
{
    private readonly Dictionary<string, string> _options;

    public CommandContext(CliCommand command, IReadOnlyList<string> arguments, Dictionary<string, string> options)
    {
        Command = command;
        Arguments = arguments;
        _options = options;
    }

    public CliCommand Command { get; }
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>True when a flag option is present.</summary>
    public bool Flag(string name) =>
        _options.TryGetValue(name, out var value) &&
        (value == "true" || bool.TryParse(value, out var parsed) && parsed);

    /// <summary>Value of an option, or null when it was not supplied.</summary>
    public string? Opt(string name) =>
        _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>Positional argument or an error when it is missing.</summary>
    public string RequiredArgument(int index, string description) =>
        index < Arguments.Count
            ? Arguments[index]
            : throw new BinPmException($"Missing argument: {description}. Run 'binpm {Command.Name} --help' for usage.");
}
