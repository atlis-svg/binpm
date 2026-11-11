using System.Runtime.InteropServices;
using BinPm.Cli.Cli;
using BinPm.Core.Configuration;
using BinPm.Core.Models;

namespace BinPm.Cli.Commands;

public static class DoctorCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>();

    public static Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var configPath = context.Opt("config") ?? BinPmConfig.DefaultConfigPath;

        Output.Write($"BinPm {Program.Version} — doctor", ConsoleColor.Cyan);
        Output.Write("");
        Output.Write($"Platform:      {Platform.Current}");
        Output.Write($"Root:          {services.Paths.Root}");
        Output.Write($"Packages dir:  {services.Paths.PackagesDir}");
        Output.Write($"Shim dir:      {services.Paths.BinDir}");
        Output.Write($"Cache dir:     {services.Paths.CacheDir}");
        Output.Write($"Config:        {configPath}");
        Output.Write($"Installed:     {services.Installer.GetInstalled().Count} package(s)");
        Output.Write($"Cache size:    {Util.FormatBytes(Util.DirectorySize(services.Paths.CacheDir))}");

        if (services.Shims.IsBinDirOnPath())
        {
            Output.Success("PATH:          shim directory is on PATH");
        }
        else
        {
            Output.Warning("PATH:          shim directory is NOT on PATH");
            Output.Write($"Add it with: {services.Shims.GetPathInstructions(Util.DetectShell())}");
        }

        try
        {
            var probe = Path.Combine(services.Paths.Root, $".binpm-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            Output.Success("Write access:  OK");
        }
        catch (Exception ex)
        {
            Output.Warning($"Write access:  FAILED ({ex.Message})");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            !services.Shims.IsBinDirOnPath())
        {
            Output.Write("");
            Output.Info("Run 'binpm init' for shell setup instructions.");
        }

        return Task.FromResult(0);
    }
}
