using BinPm.Core.Install;

namespace BinPm.Cli.Commands;

internal static class CommandHelpers
{
    public static void PrintInstallResult(AppServices services, InstallResult result)
    {
        if (result.AlreadyInstalled)
        {
            Output.Info($"{result.Record.Name} {result.Record.Version} is already installed.");
            return;
        }

        Output.Success($"{result.Record.Name} {result.Record.Version} installed.");

        if (result.Shims.Count > 0)
        {
            Output.Info($"Commands: {string.Join(", ", result.Shims.Select(shim => shim.Name))}");

            if (!services.Shims.IsBinDirOnPath())
            {
                Output.Warning($"'{services.Paths.BinDir}' is not on your PATH.");
                Output.Info($"Add it to your shell: {services.Shims.GetPathInstructions(Util.DetectShell())}");
            }
        }
        else
        {
            Output.Verbose($"Installed to {result.Record.InstallDir}");
        }
    }
}
