using System.Diagnostics.CodeAnalysis;

namespace BinPm.Cli.Cli;

public sealed class CliParseResult
{
    public CliCommand? Command { get; set; }
    public CommandContext? Context { get; set; }
    public bool HelpRequested { get; set; }
    public bool VersionRequested { get; set; }
    public string? Error { get; set; }
}

/// <summary>A small, dependency-free command line parser.</summary>
public sealed class CliParser
{
    private readonly List<CliCommand> _commands = new();

    private readonly List<CliOption> _globalOptions = new()
    {
        new CliOption("help", false, "Show help.", "h"),
        new CliOption("version", false, "Show the BinPm version.", "V"),
        new CliOption("root", true, "Override the binpm root directory."),
        new CliOption("config", true, "Path to the config file."),
        new CliOption("bin-dir", true, "Override the shim directory."),
        new CliOption("verbose", false, "Show detailed output."),
        new CliOption("no-color", false, "Disable colored output."),
        new CliOption("yes", false, "Answer yes to prompts.", "y"),
        new CliOption("offline", false, "Only use cached downloads."),
    };

    public IReadOnlyList<CliOption> GlobalOptions => _globalOptions;

    public IReadOnlyList<CliCommand> Commands => _commands;

    public CliParser Register(CliCommand command)
    {
        _commands.Add(command);
        return this;
    }

    public CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
            return new CliParseResult { HelpRequested = true };

        var first = args[0];

        if (first is "--help" or "-h" or "help")
        {
            var command = args.Length > 1 && first == "help"
                ? FindCommand(args[1])
                : null;
            return new CliParseResult { HelpRequested = true, Command = command };
        }

        if (first is "--version" or "-V" or "version")
            return new CliParseResult { VersionRequested = true };

        var matched = FindCommand(first);
        if (matched is null)
        {
            return new CliParseResult
            {
                Error = $"Unknown command '{first}'. Run 'binpm --help' to see the available commands.",
            };
        }

        var allOptions = matched.Options.Concat(_globalOptions).ToList();
        var parsedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var arguments = new List<string>();
        var helpRequested = false;

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];

            if (token == "--")
            {
                for (var j = i + 1; j < args.Length; j++)
                    arguments.Add(args[j]);
                break;
            }

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token.Substring(2);
                string? inlineValue = null;
                var equals = body.IndexOf('=');
                if (equals >= 0)
                {
                    inlineValue = body.Substring(equals + 1);
                    body = body.Substring(0, equals);
                }

                if (!TryFindOption(body, allOptions, out var option))
                {
                    return new CliParseResult
                    {
                        Error = $"Unknown option '--{body}' for command '{matched.Name}'. " +
                                $"Run 'binpm {matched.Name} --help' for usage.",
                    };
                }

                if (option.Name == "help")
                {
                    helpRequested = true;
                    continue;
                }

                if (option.TakesValue)
                {
                    string value;
                    if (inlineValue is not null)
                    {
                        value = inlineValue;
                    }
                    else if (i + 1 < args.Length && !IsOptionLike(args[i + 1]))
                    {
                        value = args[++i];
                    }
                    else
                    {
                        return new CliParseResult { Error = $"Option '--{option.Name}' requires a value." };
                    }

                    parsedOptions[option.Name] = value;
                }
                else
                {
                    if (inlineValue is not null)
                    {
                        return new CliParseResult { Error = $"Option '--{option.Name}' does not take a value." };
                    }

                    parsedOptions[option.Name] = "true";
                }

                continue;
            }

            if (token.StartsWith('-') && token.Length > 1)
            {
                var shortName = token.Substring(1);
                if (!TryFindOption(shortName, allOptions, out var shortOption))
                {
                    return new CliParseResult
                    {
                        Error = $"Unknown option '-{shortName}' for command '{matched.Name}'.",
                    };
                }

                if (shortOption.Name == "help")
                {
                    helpRequested = true;
                    continue;
                }

                if (shortOption.TakesValue)
                {
                    if (i + 1 < args.Length)
                        parsedOptions[shortOption.Name] = args[++i];
                    else
                        return new CliParseResult { Error = $"Option '-{shortName}' requires a value." };
                }
                else
                {
                    parsedOptions[shortOption.Name] = "true";
                }

                continue;
            }

            arguments.Add(token);
        }

        if (helpRequested)
            return new CliParseResult { Command = matched, HelpRequested = true };

        return new CliParseResult
        {
            Command = matched,
            Context = new CommandContext(matched, arguments, parsedOptions),
        };
    }

    private CliCommand? FindCommand(string name) =>
        _commands.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static bool TryFindOption(string name, IEnumerable<CliOption> options, [NotNullWhen(true)] out CliOption? option)
    {
        foreach (var candidate in options)
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                candidate.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                option = candidate;
                return true;
            }
        }

        option = null;
        return false;
    }

    private static bool IsOptionLike(string token) =>
        token.StartsWith("--", StringComparison.Ordinal) ||
        (token.StartsWith('-') && token.Length == 2);
}
