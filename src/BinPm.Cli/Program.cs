using BinPm.Cli.Cli;
using BinPm.Cli.Commands;
using BinPm.Core;

namespace BinPm.Cli;

public static class Program
{
    public static string Version => "0.1.0";

    public static async Task<int> Main(string[] args)
    {
        Output.Initialize(args.Contains("--no-color", StringComparer.Ordinal));

        try
        {
            return await RunAsync(args);
        }
        catch (BinPmException ex)
        {
            Output.Error(ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Output.Error($"Network error: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Output.Error("Operation cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Output.Error($"Unexpected error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("BINPM_VERBOSE") == "1")
                Output.Write(ex.ToString());
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var parser = BuildParser();
        var result = parser.Parse(args);

        if (result.VersionRequested)
        {
            Output.Write($"BinPm {Version}");
            return 0;
        }

        if (result.Error is not null)
        {
            Output.Error(result.Error);
            return 2;
        }

        if (result.HelpRequested)
        {
            PrintHelp(parser, result.Command);
            return 0;
        }

        Output.IsVerbose = result.Context!.Flag("verbose");
        return await result.Command!.Handler(result.Context);
    }

    private static CliParser BuildParser() => new CliParser()
        .Register(new CliCommand(
            "install",
            "Install a package.",
            "binpm install <package>[@version] [options]",
            InstallCommand.Options,
            InstallCommand.HandleAsync,
            "add", "i"))
        .Register(new CliCommand(
            "uninstall",
            "Uninstall a package.",
            "binpm uninstall <package>",
            UninstallCommand.Options,
            UninstallCommand.HandleAsync,
            "remove", "rm"))
        .Register(new CliCommand(
            "update",
            "Update one or all installed packages.",
            "binpm update [package] [options]",
            UpdateCommand.Options,
            UpdateCommand.HandleAsync,
            "upgrade", "up"))
        .Register(new CliCommand(
            "list",
            "List installed packages (or available ones with --available).",
            "binpm list [--available] [--outdated]",
            ListCommand.Options,
            ListCommand.HandleAsync,
            "ls"))
        .Register(new CliCommand(
            "search",
            "Search configured registries and GitHub for packages.",
            "binpm search <term>",
            SearchCommand.Options,
            SearchCommand.HandleAsync,
            "find"))
        .Register(new CliCommand(
            "info",
            "Show details about a package.",
            "binpm info <package>",
            InfoCommand.Options,
            InfoCommand.HandleAsync,
            "show"))
        .Register(new CliCommand(
            "init",
            "Show PATH setup instructions for your shell.",
            "binpm init [--shell bash|zsh|fish|powershell|cmd]",
            InitCommand.Options,
            InitCommand.HandleAsync))
        .Register(new CliCommand(
            "doctor",
            "Check the installation and show diagnostics.",
            "binpm doctor",
            DoctorCommand.Options,
            DoctorCommand.HandleAsync))
        .Register(new CliCommand(
            "cache",
            "Manage the download cache.",
            "binpm cache clean",
            CacheCommand.Options,
            CacheCommand.HandleAsync))
        .Register(new CliCommand(
            "which",
            "Show which installed package provides a command.",
            "binpm which <command>",
            WhichCommand.Options,
            WhichCommand.HandleAsync));

    private static void PrintHelp(CliParser parser, CliCommand? command)
    {
        if (command is null)
        {
            Output.Write($"BinPm {Version} — a package manager for binaries", ConsoleColor.Cyan);
            Output.Write("");
            Output.Write("Usage: binpm <command> [options] [arguments]");
            Output.Write("");
            Output.Write("Commands:", ConsoleColor.Cyan);

            foreach (var cmd in parser.Commands)
            {
                var name = cmd.Name.PadRight(12);
                Output.Write($"  {name} {cmd.Description}");
            }

            Output.Write("");
            Output.Write("Global options:", ConsoleColor.Cyan);
            PrintOptions(parser.GlobalOptions);
            Output.Write("");
            Output.Write("Run 'binpm <command> --help' for command-specific help.");
            return;
        }

        Output.Write($"{command.Name} — {command.Description}", ConsoleColor.Cyan);
        Output.Write("");
        Output.Write($"Usage: {command.Usage}");

        if (command.Options.Count > 0)
        {
            Output.Write("");
            Output.Write("Options:", ConsoleColor.Cyan);
            PrintOptions(command.Options);
        }

        var global = parser.GlobalOptions
            .Where(o => o.Name is not "help" and not "version")
            .ToList();

        Output.Write("");
        Output.Write("Global options:", ConsoleColor.Cyan);
        PrintOptions(global);
    }

    private static void PrintOptions(IReadOnlyList<CliOption> options)
    {
        foreach (var option in options)
        {
            var left = option.TakesValue ? $"--{option.Name} <value>" : $"--{option.Name}";

            foreach (var alias in option.Aliases)
                left += $", -{alias}" + (option.TakesValue ? " <value>" : "");

            Output.Write($"  {left.PadRight(26)} {option.Description}");
        }
    }
}
