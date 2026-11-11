using System.Runtime.InteropServices;
using BinPm.Cli.Cli;

namespace BinPm.Cli.Commands;

public static class WhichCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>();

    public static Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var name = context.RequiredArgument(0, "command");

        var shimPath = services.Shims.GetShimPath(name);
        var target = services.Shims.GetShimTarget(shimPath);

        if (target is null)
        {
            Output.Warning($"No shim named '{name}' in '{services.Paths.BinDir}'.");
            Output.Info("Is the package installed? Try 'binpm list'.");
            return Task.FromResult(1);
        }

        Output.Write(target);

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var package in services.Installer.GetInstalled())
        {
            if (target.StartsWith(package.InstallDir + Path.DirectorySeparatorChar, comparison))
            {
                Output.Info($"Provided by {package.Name} {package.Version} ({package.Source})");
                break;
            }
        }

        return Task.FromResult(0);
    }
}
