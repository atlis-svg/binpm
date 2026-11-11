using BinPm.Cli.Cli;
using BinPm.Core.Configuration;

namespace BinPm.Cli.Commands;

public static class InitCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("shell", true, "Shell to print instructions for: bash, zsh, fish, powershell, cmd."),
    };

    public static Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var shell = context.Opt("shell") ?? Util.DetectShell();
        var configPath = context.Opt("config") ?? BinPmConfig.DefaultConfigPath;

        Output.Write($"BinPm {Program.Version}", ConsoleColor.Cyan);
        Output.Write("");
        Output.Write($"Root:        {services.Paths.Root}");
        Output.Write($"Packages:    {services.Paths.PackagesDir}");
        Output.Write($"Shims:       {services.Paths.BinDir}");
        Output.Write($"Cache:       {services.Paths.CacheDir}");
        Output.Write($"Config:      {configPath}");
        Output.Write("");

        if (services.Shims.IsBinDirOnPath())
        {
            Output.Success($"'{services.Paths.BinDir}' is already on your PATH.");
        }
        else
        {
            Output.Warning($"'{services.Paths.BinDir}' is not on your PATH.");
            Output.Write("Add it by running:", ConsoleColor.Cyan);
            Output.Write($"  {services.Shims.GetPathInstructions(shell)}");
        }

        return Task.FromResult(0);
    }
}
