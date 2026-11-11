using BinPm.Cli.Cli;
using BinPm.Core.Install;

namespace BinPm.Cli.Commands;

public static class UninstallCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("no-shim", false, "Keep the shims (only remove the package files)."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var name = context.RequiredArgument(0, "package");

        var installed = services.Installer.GetInstalled(name)
            ?? services.Installer.Store.FindByReference(name);

        if (installed is null)
        {
            Output.Warning($"Package '{name}' is not installed.");
            return 0;
        }

        var binNames = installed.Bins
            .Select(BinaryInstaller.GetBinName)
            .ToList();

        await services.Installer.UninstallAsync(name, removeShims: !context.Flag("no-shim"));

        Output.Success($"Uninstalled {installed.Name} {installed.Version}.");
        if (binNames.Count > 0 && !context.Flag("no-shim"))
            Output.Info($"Removed commands: {string.Join(", ", binNames)}");

        return 0;
    }
}
